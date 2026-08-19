using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SchoolPiBoard.Online.Data;
using SchoolPiBoard.Online.Services;

namespace SchoolPiBoard.Online.Realtime;

public sealed record ParticipantDto(Guid UserId, string Name, string Color, string Role);

public sealed record BoardMemberDto(Guid UserId, string Email, string Name, string Role);

/// <summary>
/// Ответ на JoinBoard: состояние комнаты целиком. Именно это делает простым
/// переподключение — клиент не «догоняет» пропущенные события, а получает
/// актуальное состояние заново.
/// </summary>
public sealed record BoardJoinedDto(
    Guid BoardId,
    string Name,
    string Role,
    bool CanEdit,
    bool CanManage,
    IReadOnlyList<ParticipantDto> Participants,
    IReadOnlyList<BoardMemberDto> Members);

public sealed record CursorDto(Guid UserId, double X, double Y);

/// <summary>
/// Комната доски. Сервер — источник истины: роль проверяется здесь при каждом
/// обращении. Скрытая на фронтенде кнопка — удобство, а не защита.
/// </summary>
[Authorize]
public sealed class BoardHub : Hub
{
    public const string Path = "/hub/board";

    private const string JoinedKey = "joined-boards";

    private readonly BoardService _boards;
    private readonly IPresenceStore _presence;

    public BoardHub(BoardService boards, IPresenceStore presence)
    {
        _boards = boards;
        _presence = presence;
    }

    public static string GroupName(Guid boardId) => $"board:{boardId}";

    public async Task<BoardJoinedDto> JoinBoard(Guid boardId)
    {
        var userId = RequireUserId();

        var role = await _boards.GetRoleAsync(boardId, userId, Context.ConnectionAborted);
        if (role is null)
            throw new HubException("Нет доступа к этой доске.");

        var board = await _boards.GetAsync(boardId, userId, Context.ConnectionAborted);
        if (!board.IsOk || board.Value is null)
            throw new HubException("Доска не найдена.");

        var members = await _boards.ListMembersAsync(boardId, userId, Context.ConnectionAborted);

        var entry = new PresenceEntry(
            Context.ConnectionId,
            userId,
            Context.User.UserDisplayName(),
            UserColor.For(userId),
            BoardRoles.ToName(role.Value));

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(boardId), Context.ConnectionAborted);
        await _presence.JoinAsync(boardId, entry);
        JoinedBoards().Add(boardId);

        var participants = Distinct(await _presence.ListAsync(boardId));

        await Clients.OthersInGroup(GroupName(boardId))
            .SendAsync("UserJoined", ToParticipant(entry), Context.ConnectionAborted);

        var now = DateTime.UtcNow;

        return new BoardJoinedDto(
            board.Value.Id,
            board.Value.Name,
            BoardRoles.ToName(role.Value),
            BoardRoles.CanEdit(role.Value),
            BoardRoles.CanManage(role.Value),
            participants,
            (members.Value ?? Array.Empty<BoardMember>())
                .Select(member => new BoardMemberDto(
                    member.UserId,
                    member.User?.Email ?? string.Empty,
                    member.User?.FullName ?? "Участник",
                    BoardRoles.ToName(member.EffectiveRole(now))))
                .ToList());
    }

    public async Task LeaveBoard(Guid boardId)
    {
        JoinedBoards().Remove(boardId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(boardId), Context.ConnectionAborted);
        await AnnounceLeaveAsync(boardId);
    }

    /// <summary>
    /// Курсор участника. Событие частое, поэтому никуда не сохраняется —
    /// только рассылается остальным. Поток прореживает клиент.
    /// </summary>
    public async Task CursorMove(Guid boardId, double x, double y)
    {
        var userId = RequireUserId();

        // В доску, куда не входил, слать нельзя: иначе чужую комнату можно
        // было бы засыпать событиями, зная только её идентификатор.
        if (!JoinedBoards().Contains(boardId))
            return;

        await Clients.OthersInGroup(GroupName(boardId))
            .SendAsync("CursorMoved", new CursorDto(userId, x, y), Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var boardId in JoinedBoards().ToList())
            await AnnounceLeaveAsync(boardId);

        await base.OnDisconnectedAsync(exception);
    }

    private async Task AnnounceLeaveAsync(Guid boardId)
    {
        var left = await _presence.LeaveAsync(boardId, Context.ConnectionId);
        if (left is null)
            return;

        // Вторая вкладка того же человека могла остаться открытой —
        // тогда для остальных он из доски не уходил.
        var remaining = await _presence.ListAsync(boardId);
        if (remaining.Any(x => x.UserId == left.UserId))
            return;

        await Clients.Group(GroupName(boardId)).SendAsync("UserLeft", left.UserId);
    }

    private Guid RequireUserId()
        => Context.User.UserId() ?? throw new HubException("Не удалось определить пользователя.");

    private HashSet<Guid> JoinedBoards()
    {
        if (Context.Items.TryGetValue(JoinedKey, out var value) && value is HashSet<Guid> joined)
            return joined;

        var created = new HashSet<Guid>();
        Context.Items[JoinedKey] = created;
        return created;
    }

    private static IReadOnlyList<ParticipantDto> Distinct(IReadOnlyList<PresenceEntry> entries)
        => entries.GroupBy(x => x.UserId).Select(group => ToParticipant(group.First())).ToList();

    private static ParticipantDto ToParticipant(PresenceEntry entry)
        => new(entry.UserId, entry.Name, entry.Color, entry.Role);
}
