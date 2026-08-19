using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Whiteboard.LicenseServer.Data;
using Whiteboard.LicenseServer.Services;

namespace Whiteboard.LicenseServer.Realtime;

/// <summary>
/// Комната доски. Сервер — источник истины: роль участника проверяется здесь
/// при каждом обращении, а не на клиенте. Скрытая на фронтенде кнопка
/// «нарисовать» — это удобство, а не защита.
/// </summary>
[Authorize]
public sealed class BoardHub : Hub
{
    public const string Path = "/hub/board";

    private const string JoinedKey = "joined-boards";

    private readonly BoardService _boards;
    private readonly IPresenceStore _presence;
    private readonly ILogger<BoardHub> _logger;

    public BoardHub(BoardService boards, IPresenceStore presence, ILogger<BoardHub> logger)
    {
        _boards = boards;
        _presence = presence;
        _logger = logger;
    }

    public static string GroupName(Guid boardId) => $"board:{boardId}";

    /// <summary>
    /// Вход в комнату. Возвращает всё состояние сразу — тот же вызов
    /// используется при переподключении после обрыва связи.
    /// </summary>
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
                    string.IsNullOrWhiteSpace(member.User?.DisplayName)
                        ? NameFromEmail(member.User?.Email)
                        : member.User!.DisplayName,
                    member.Role))
                .ToList());
    }

    public async Task LeaveBoard(Guid boardId)
    {
        JoinedBoards().Remove(boardId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(boardId), Context.ConnectionAborted);
        await AnnounceLeaveAsync(boardId);
    }

    /// <summary>
    /// Курсор участника. Событие частое, поэтому в базу не пишется и никак
    /// не сохраняется — только рассылается остальным. Прореживает поток
    /// клиент (15–20 раз в секунду), сервер здесь лишь передаёт.
    /// </summary>
    public async Task CursorMove(Guid boardId, double x, double y)
    {
        var userId = RequireUserId();

        // Отправлять курсор в доску, куда не входил, нельзя: иначе чужую
        // комнату можно было бы засыпать событиями, зная её идентификатор.
        if (!JoinedBoards().Contains(boardId))
            return;

        await Clients.OthersInGroup(GroupName(boardId))
            .SendAsync("CursorMoved", new CursorDto(userId, x, y), Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Снимаем участника со всех досок, куда он входил в этом соединении.
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

    /// <summary>Одна строка на человека, даже если у него открыто несколько вкладок.</summary>
    private static IReadOnlyList<ParticipantDto> Distinct(IReadOnlyList<PresenceEntry> entries)
        => entries
            .GroupBy(x => x.UserId)
            .Select(group => ToParticipant(group.First()))
            .ToList();

    private static ParticipantDto ToParticipant(PresenceEntry entry)
        => new(entry.UserId, entry.Name, entry.Color, entry.Role);

    private static string NameFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "Участник";

        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}
