using Microsoft.EntityFrameworkCore;
using Whiteboard.LicenseServer.Data;

namespace Whiteboard.LicenseServer.Services;

public enum BoardOutcome
{
    Ok,

    /// <summary>Доски нет либо пользователь не её участник — снаружи это одно и то же.</summary>
    NotFound,

    /// <summary>Участник есть, но роль не позволяет.</summary>
    Forbidden,

    /// <summary>Приглашаемый ещё не зарегистрирован.</summary>
    UserNotFound,

    BadRequest
}

public sealed record BoardSummary(Board Board, BoardRole Role, int MemberCount);

public sealed record BoardResult<T>(BoardOutcome Outcome, T? Value, string? Message = null)
{
    public bool IsOk => Outcome == BoardOutcome.Ok;
}

/// <summary>
/// Доски и участники. Роль пользователя всегда берётся отсюда — и REST,
/// и SignalR-хаб спрашивают один и тот же метод, чтобы правила доступа
/// не разъезжались между ними.
/// </summary>
public sealed class BoardService
{
    private readonly LicenseDbContext _db;
    private readonly ILogger<BoardService> _logger;

    public BoardService(LicenseDbContext db, ILogger<BoardService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Роль пользователя на доске или null, если он не участник.</summary>
    public async Task<BoardRole?> GetRoleAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var member = await _db.BoardMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == userId, cancellationToken);

        if (member is null)
            return null;

        return BoardRoles.TryParse(member.Role, out var role) ? role : BoardRole.Viewer;
    }

    public async Task<IReadOnlyList<BoardSummary>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await _db.BoardMembers
            .AsNoTracking()
            .Where(member => member.UserId == userId)
            .Join(_db.Boards.AsNoTracking(),
                  member => member.BoardId,
                  board => board.Id,
                  (member, board) => new { member.Role, Board = board })
            .OrderByDescending(x => x.Board.ModifiedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return Array.Empty<BoardSummary>();

        var boardIds = rows.Select(x => x.Board.Id).ToList();

        var counts = await _db.BoardMembers
            .AsNoTracking()
            .Where(x => boardIds.Contains(x.BoardId))
            .GroupBy(x => x.BoardId)
            .Select(group => new { BoardId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.BoardId, x => x.Count, cancellationToken);

        return rows
            .Select(row => new BoardSummary(
                row.Board,
                BoardRoles.TryParse(row.Role, out var role) ? role : BoardRole.Viewer,
                counts.TryGetValue(row.Board.Id, out var count) ? count : 1))
            .ToList();
    }

    public async Task<BoardResult<Board>> CreateAsync(Guid userId, string? name, CancellationToken cancellationToken)
    {
        var title = string.IsNullOrWhiteSpace(name) ? "Новая доска" : name.Trim();
        if (title.Length > 200)
            title = title[..200];

        var board = new Board
        {
            OwnerId = userId,
            Name = title,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        // Владелец записывается и в доску, и в участники: тогда роль
        // определяется одним запросом к board_members без особых случаев.
        board.Members.Add(new BoardMember
        {
            BoardId = board.Id,
            UserId = userId,
            Role = BoardRoles.Owner,
            InvitedAt = DateTime.UtcNow
        });

        _db.Boards.Add(board);
        await _db.SaveChangesAsync(cancellationToken);

        return new BoardResult<Board>(BoardOutcome.Ok, board);
    }

    public async Task<BoardResult<Board>> GetAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var role = await GetRoleAsync(boardId, userId, cancellationToken);
        if (role is null)
            return new BoardResult<Board>(BoardOutcome.NotFound, null, "Доска не найдена.");

        var board = await _db.Boards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);
        return board is null
            ? new BoardResult<Board>(BoardOutcome.NotFound, null, "Доска не найдена.")
            : new BoardResult<Board>(BoardOutcome.Ok, board);
    }

    public async Task<BoardOutcome> DeleteAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var role = await GetRoleAsync(boardId, userId, cancellationToken);
        if (role is null)
            return BoardOutcome.NotFound;

        if (!BoardRoles.CanManage(role.Value))
            return BoardOutcome.Forbidden;

        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);
        if (board is null)
            return BoardOutcome.NotFound;

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Удалена доска {BoardId}.", boardId);
        return BoardOutcome.Ok;
    }

    public async Task<BoardResult<IReadOnlyList<BoardMember>>> ListMembersAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var role = await GetRoleAsync(boardId, userId, cancellationToken);
        if (role is null)
            return new BoardResult<IReadOnlyList<BoardMember>>(BoardOutcome.NotFound, null, "Доска не найдена.");

        var members = await _db.BoardMembers
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.BoardId == boardId)
            .OrderBy(x => x.InvitedAt)
            .ToListAsync(cancellationToken);

        return new BoardResult<IReadOnlyList<BoardMember>>(BoardOutcome.Ok, members);
    }

    public async Task<BoardResult<BoardMember>> AddMemberAsync(
        Guid boardId, Guid actorId, string? email, string? roleName, CancellationToken cancellationToken)
    {
        var actorRole = await GetRoleAsync(boardId, actorId, cancellationToken);
        if (actorRole is null)
            return new BoardResult<BoardMember>(BoardOutcome.NotFound, null, "Доска не найдена.");

        if (!BoardRoles.CanManage(actorRole.Value))
            return new BoardResult<BoardMember>(BoardOutcome.Forbidden, null, "Приглашать участников может только владелец доски.");

        if (!BoardRoles.TryParse(roleName, out var role) || role == BoardRole.Owner)
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, null, "Роль должна быть editor или viewer.");

        var address = EmailAddress.Normalize(email);
        if (address is null)
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, null, "Проверьте адрес почты.");

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == address, cancellationToken);
        if (user is null)
        {
            // Приглашения по ссылке без регистрации — за рамками этого этапа,
            // поэтому пригласить можно только уже зарегистрированного человека.
            return new BoardResult<BoardMember>(BoardOutcome.UserNotFound, null,
                "Такой пользователь не зарегистрирован. Попросите его создать учётную запись.");
        }

        var existing = await _db.BoardMembers
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == user.Id, cancellationToken);

        if (existing is not null)
        {
            // Повторное приглашение — это изменение роли, а не ошибка.
            if (BoardRoles.TryParse(existing.Role, out var current) && current == BoardRole.Owner)
                return new BoardResult<BoardMember>(BoardOutcome.BadRequest, null, "Это владелец доски.");

            existing.Role = BoardRoles.ToName(role);
            await _db.SaveChangesAsync(cancellationToken);

            existing.User = user;
            return new BoardResult<BoardMember>(BoardOutcome.Ok, existing);
        }

        var member = new BoardMember
        {
            BoardId = boardId,
            UserId = user.Id,
            Role = BoardRoles.ToName(role),
            InvitedAt = DateTime.UtcNow
        };

        _db.BoardMembers.Add(member);
        await _db.SaveChangesAsync(cancellationToken);

        member.User = user;
        return new BoardResult<BoardMember>(BoardOutcome.Ok, member);
    }

    public async Task<BoardResult<BoardMember>> ChangeRoleAsync(
        Guid boardId, Guid actorId, Guid targetUserId, string? roleName, CancellationToken cancellationToken)
    {
        var actorRole = await GetRoleAsync(boardId, actorId, cancellationToken);
        if (actorRole is null)
            return new BoardResult<BoardMember>(BoardOutcome.NotFound, null, "Доска не найдена.");

        if (!BoardRoles.CanManage(actorRole.Value))
            return new BoardResult<BoardMember>(BoardOutcome.Forbidden, null, "Менять роли может только владелец доски.");

        if (!BoardRoles.TryParse(roleName, out var role) || role == BoardRole.Owner)
        {
            // Передача владения — отдельная операция со своими последствиями,
            // сейчас её нет.
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, null, "Роль должна быть editor или viewer.");
        }

        var member = await _db.BoardMembers
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new BoardResult<BoardMember>(BoardOutcome.NotFound, null, "Участник не найден.");

        if (BoardRoles.TryParse(member.Role, out var currentRole) && currentRole == BoardRole.Owner)
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, null, "Роль владельца изменить нельзя.");

        member.Role = BoardRoles.ToName(role);
        await _db.SaveChangesAsync(cancellationToken);

        return new BoardResult<BoardMember>(BoardOutcome.Ok, member);
    }

    public async Task<BoardResult<Guid>> RemoveMemberAsync(
        Guid boardId, Guid actorId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var actorRole = await GetRoleAsync(boardId, actorId, cancellationToken);
        if (actorRole is null)
            return new BoardResult<Guid>(BoardOutcome.NotFound, default, "Доска не найдена.");

        // Уйти с доски может каждый сам; убирать других — только владелец.
        var removingSelf = actorId == targetUserId;
        if (!removingSelf && !BoardRoles.CanManage(actorRole.Value))
            return new BoardResult<Guid>(BoardOutcome.Forbidden, default, "Убирать участников может только владелец доски.");

        var member = await _db.BoardMembers
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new BoardResult<Guid>(BoardOutcome.NotFound, default, "Участник не найден.");

        if (BoardRoles.TryParse(member.Role, out var role) && role == BoardRole.Owner)
        {
            return new BoardResult<Guid>(BoardOutcome.BadRequest, default,
                "Владельца нельзя убрать из доски — доску можно только удалить.");
        }

        _db.BoardMembers.Remove(member);
        await _db.SaveChangesAsync(cancellationToken);

        return new BoardResult<Guid>(BoardOutcome.Ok, targetUserId);
    }

    /// <summary>Отмечает доску изменённой — вызывается при правках содержимого.</summary>
    public async Task TouchAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);
        if (board is null)
            return;

        board.ModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
