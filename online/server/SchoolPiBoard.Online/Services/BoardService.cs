using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.Online.Configuration;
using SchoolPiBoard.Online.Data;

namespace SchoolPiBoard.Online.Services;

public enum BoardOutcome
{
    Ok,
    NotFound,
    Forbidden,
    UserNotFound,
    BadRequest,
    /// <summary>Ссылка-приглашение просрочена или отозвана.</summary>
    InviteExpired
}

public sealed record BoardResult<T>(BoardOutcome Outcome, T? Value = default, string? Message = null)
{
    public bool IsOk => Outcome == BoardOutcome.Ok;
}

/// <summary>Строка списка «Мои доски».</summary>
public sealed record BoardListItem(Board Board, BoardRole Role, bool Invited, int MemberCount, DateTime? EditUntil);

public sealed record BoardPage(IReadOnlyList<BoardListItem> Items, int Page, int PageSize, int Total);

public sealed record InviteLink(BoardInvite Invite, string Url);

/// <summary>
/// Доски, участники и приглашения.
///
/// Роль всегда берётся отсюда: и REST, и хаб спрашивают один и тот же метод,
/// иначе правила доступа неизбежно разъедутся между ними.
/// </summary>
public sealed class BoardService
{
    /// <summary>По ТЗ на странице «Мои доски» — десять досок.</summary>
    public const int DefaultPageSize = 10;

    private readonly AppDbContext _db;
    private readonly OnlineOptions _options;
    private readonly ILogger<BoardService> _logger;

    public BoardService(AppDbContext db, OnlineOptions options, ILogger<BoardService> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    /// <summary>Роль с учётом того, что право правки по ссылке могло истечь.</summary>
    public async Task<BoardRole?> GetRoleAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var member = await _db.BoardMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == userId, cancellationToken);

        return member?.EffectiveRole(DateTime.UtcNow);
    }

    public async Task<BoardPage> ListAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 50 ? DefaultPageSize : pageSize;

        var query = _db.BoardMembers
            .AsNoTracking()
            .Where(member => member.UserId == userId)
            .Join(_db.Boards.AsNoTracking(),
                  member => member.BoardId,
                  board => board.Id,
                  (member, board) => new { Member = member, Board = board });

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.Board.ModifiedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return new BoardPage(Array.Empty<BoardListItem>(), page, pageSize, total);

        var boardIds = rows.Select(x => x.Board.Id).ToList();

        var counts = await _db.BoardMembers
            .AsNoTracking()
            .Where(x => boardIds.Contains(x.BoardId))
            .GroupBy(x => x.BoardId)
            .Select(group => new { BoardId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.BoardId, x => x.Count, cancellationToken);

        var now = DateTime.UtcNow;

        var items = rows
            .Select(row => new BoardListItem(
                row.Board,
                row.Member.EffectiveRole(now),
                row.Board.OwnerId != userId,
                counts.TryGetValue(row.Board.Id, out var count) ? count : 1,
                row.Member.EditUntil))
            .ToList();

        return new BoardPage(items, page, pageSize, total);
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
        // определяется одним запросом, без особых случаев.
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
            return new BoardResult<Board>(BoardOutcome.NotFound, Message: "Доска не найдена.");

        var board = await _db.Boards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);

        return board is null
            ? new BoardResult<Board>(BoardOutcome.NotFound, Message: "Доска не найдена.")
            : new BoardResult<Board>(BoardOutcome.Ok, board);
    }

    public async Task<BoardOutcome> DeleteAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);
        if (board is null)
            return BoardOutcome.NotFound;

        if (board.OwnerId != userId)
            return BoardOutcome.Forbidden;

        _db.Boards.Remove(board);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Удалена доска {BoardId}.", boardId);
        return BoardOutcome.Ok;
    }

    public async Task<BoardResult<IReadOnlyList<BoardMember>>> ListMembersAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var role = await GetRoleAsync(boardId, userId, cancellationToken);
        if (role is null)
            return new BoardResult<IReadOnlyList<BoardMember>>(BoardOutcome.NotFound, Message: "Доска не найдена.");

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
        var check = await RequireOwnerAsync(boardId, actorId, cancellationToken);
        if (check is not null)
            return new BoardResult<BoardMember>(check.Value, Message: OwnerMessage(check.Value));

        if (!BoardRoles.TryParse(roleName, out var role) || role == BoardRole.Owner)
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, Message: "Роль должна быть editor или viewer.");

        var address = EmailAddress.Normalize(email);
        if (address is null)
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, Message: "Проверьте адрес почты.");

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == address, cancellationToken);
        if (user is null)
        {
            return new BoardResult<BoardMember>(BoardOutcome.UserNotFound,
                Message: "Такой пользователь не зарегистрирован. Пришлите ему ссылку-приглашение.");
        }

        var existing = await _db.BoardMembers
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == user.Id, cancellationToken);

        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            if (BoardRoles.TryParse(existing.Role, out var current) && current == BoardRole.Owner)
                return new BoardResult<BoardMember>(BoardOutcome.BadRequest, Message: "Это владелец доски.");

            // Повторное приглашение — это назначение роли заново, а значит
            // и новый отсчёт срока правок.
            existing.Role = BoardRoles.ToName(role);
            existing.ViaLink = false;
            existing.EditUntil = EditUntilFor(role, now);
            await _db.SaveChangesAsync(cancellationToken);

            existing.User = user;
            return new BoardResult<BoardMember>(BoardOutcome.Ok, existing);
        }

        var member = new BoardMember
        {
            BoardId = boardId,
            UserId = user.Id,
            Role = BoardRoles.ToName(role),
            InvitedAt = now,
            EditUntil = EditUntilFor(role, now)
        };

        _db.BoardMembers.Add(member);
        await _db.SaveChangesAsync(cancellationToken);

        member.User = user;
        return new BoardResult<BoardMember>(BoardOutcome.Ok, member);
    }

    public async Task<BoardResult<BoardMember>> ChangeRoleAsync(
        Guid boardId, Guid actorId, Guid targetUserId, string? roleName, CancellationToken cancellationToken)
    {
        var check = await RequireOwnerAsync(boardId, actorId, cancellationToken);
        if (check is not null)
            return new BoardResult<BoardMember>(check.Value, Message: OwnerMessage(check.Value));

        if (!BoardRoles.TryParse(roleName, out var role) || role == BoardRole.Owner)
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, Message: "Роль должна быть editor или viewer.");

        var member = await _db.BoardMembers
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new BoardResult<BoardMember>(BoardOutcome.NotFound, Message: "Участник не найден.");

        if (BoardRoles.TryParse(member.Role, out var currentRole) && currentRole == BoardRole.Owner)
            return new BoardResult<BoardMember>(BoardOutcome.BadRequest, Message: "Роль владельца изменить нельзя.");

        // Назначая роль заново, владелец продлевает и срок правок:
        // это единственный способ вернуть редактора после того,
        // как срок истёк.
        member.Role = BoardRoles.ToName(role);
        member.EditUntil = EditUntilFor(role, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return new BoardResult<BoardMember>(BoardOutcome.Ok, member);
    }

    public async Task<BoardResult<Guid>> RemoveMemberAsync(
        Guid boardId, Guid actorId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var board = await _db.Boards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);
        if (board is null)
            return new BoardResult<Guid>(BoardOutcome.NotFound, Message: "Доска не найдена.");

        // Уйти с доски может каждый сам; убирать других — только владелец.
        if (actorId != targetUserId && board.OwnerId != actorId)
            return new BoardResult<Guid>(BoardOutcome.Forbidden, Message: "Убирать участников может только владелец доски.");

        var member = await _db.BoardMembers
            .FirstOrDefaultAsync(x => x.BoardId == boardId && x.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new BoardResult<Guid>(BoardOutcome.NotFound, Message: "Участник не найден.");

        if (member.UserId == board.OwnerId)
            return new BoardResult<Guid>(BoardOutcome.BadRequest, Message: "Владельца нельзя убрать — доску можно только удалить.");

        _db.BoardMembers.Remove(member);
        await _db.SaveChangesAsync(cancellationToken);

        return new BoardResult<Guid>(BoardOutcome.Ok, targetUserId);
    }

    // ---------- ссылки-приглашения ----------

    public async Task<BoardResult<IReadOnlyList<BoardInvite>>> ListInvitesAsync(Guid boardId, Guid actorId, CancellationToken cancellationToken)
    {
        var check = await RequireOwnerAsync(boardId, actorId, cancellationToken);
        if (check is not null)
            return new BoardResult<IReadOnlyList<BoardInvite>>(check.Value, Message: OwnerMessage(check.Value));

        var invites = await _db.BoardInvites
            .AsNoTracking()
            .Where(x => x.BoardId == boardId && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return new BoardResult<IReadOnlyList<BoardInvite>>(BoardOutcome.Ok, invites);
    }

    /// <summary>
    /// Новая ссылка-приглашение. По ссылке можно войти ограниченное время;
    /// вошедшие сохраняют доступ и после того, как ссылка перестала работать.
    /// Право менять доску у них ограничено общим сроком для приглашённых.
    /// </summary>
    public async Task<BoardResult<InviteLink>> CreateInviteAsync(
        Guid boardId, Guid actorId, string? roleName, int? lifetimeDays, CancellationToken cancellationToken)
    {
        var check = await RequireOwnerAsync(boardId, actorId, cancellationToken);
        if (check is not null)
            return new BoardResult<InviteLink>(check.Value, Message: OwnerMessage(check.Value));

        if (!BoardRoles.TryParse(roleName, out var role) || role == BoardRole.Owner)
            return new BoardResult<InviteLink>(BoardOutcome.BadRequest, Message: "Роль должна быть editor или viewer.");

        var lifetime = lifetimeDays is > 0 and <= 365 ? lifetimeDays.Value : _options.Invites.LinkLifetimeDays;

        var token = SecurityTokens.Create();
        var now = DateTime.UtcNow;

        var invite = new BoardInvite
        {
            BoardId = boardId,
            CreatedBy = actorId,
            TokenHash = SecurityTokens.HashOf(token),
            Role = BoardRoles.ToName(role),
            CreatedAt = now,
            ExpiresAt = now.AddDays(lifetime)
        };

        _db.BoardInvites.Add(invite);
        await _db.SaveChangesAsync(cancellationToken);

        var url = $"{_options.Site.BaseUrl}/join/{token}";
        return new BoardResult<InviteLink>(BoardOutcome.Ok, new InviteLink(invite, url));
    }

    public async Task<BoardOutcome> RevokeInviteAsync(Guid boardId, Guid actorId, Guid inviteId, CancellationToken cancellationToken)
    {
        var check = await RequireOwnerAsync(boardId, actorId, cancellationToken);
        if (check is not null)
            return check.Value;

        var invite = await _db.BoardInvites.FirstOrDefaultAsync(x => x.Id == inviteId && x.BoardId == boardId, cancellationToken);
        if (invite is null)
            return BoardOutcome.NotFound;

        invite.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return BoardOutcome.Ok;
    }

    /// <summary>Что за доска стоит за ссылкой — показывается до входа.</summary>
    public async Task<BoardResult<Board>> PeekInviteAsync(string token, CancellationToken cancellationToken)
    {
        var invite = await FindInviteAsync(token, cancellationToken);
        if (invite is null)
            return new BoardResult<Board>(BoardOutcome.InviteExpired, Message: "Ссылка недействительна или устарела.");

        var board = await _db.Boards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invite.BoardId, cancellationToken);

        return board is null
            ? new BoardResult<Board>(BoardOutcome.NotFound, Message: "Доска не найдена.")
            : new BoardResult<Board>(BoardOutcome.Ok, board);
    }

    public async Task<BoardResult<Board>> JoinByInviteAsync(string token, Guid userId, CancellationToken cancellationToken)
    {
        var invite = await FindInviteAsync(token, cancellationToken);
        if (invite is null)
            return new BoardResult<Board>(BoardOutcome.InviteExpired, Message: "Ссылка недействительна или устарела.");

        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Id == invite.BoardId, cancellationToken);
        if (board is null)
            return new BoardResult<Board>(BoardOutcome.NotFound, Message: "Доска не найдена.");

        var now = DateTime.UtcNow;

        var member = await _db.BoardMembers
            .FirstOrDefaultAsync(x => x.BoardId == invite.BoardId && x.UserId == userId, cancellationToken);

        if (member is null)
        {
            BoardRoles.TryParse(invite.Role, out var role);

            _db.BoardMembers.Add(new BoardMember
            {
                BoardId = invite.BoardId,
                UserId = userId,
                Role = BoardRoles.ToName(role),
                InvitedAt = now,
                ViaLink = true,
                EditUntil = EditUntilFor(role, now)
            });

            invite.Uses += 1;
        }

        // Если участник уже есть, повторный переход по ссылке ничего не меняет:
        // вернуть право правки после истечения срока может только владелец.

        await _db.SaveChangesAsync(cancellationToken);

        return new BoardResult<Board>(BoardOutcome.Ok, board);
    }

    public async Task TouchAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var board = await _db.Boards.FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);
        if (board is null)
            return;

        board.ModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// До какого момента приглашённый может менять доску. У наблюдателя
    /// ограничивать нечего, поэтому срок только у редактора.
    /// </summary>
    private DateTime? EditUntilFor(BoardRole role, DateTime now)
        => role == BoardRole.Editor ? now.AddDays(_options.Invites.MemberEditorDays) : null;

    private async Task<BoardInvite?> FindInviteAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var hash = SecurityTokens.HashOf(token);
        var invite = await _db.BoardInvites.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        return invite is not null && invite.IsUsable(DateTime.UtcNow) ? invite : null;
    }

    private async Task<BoardOutcome?> RequireOwnerAsync(Guid boardId, Guid actorId, CancellationToken cancellationToken)
    {
        var board = await _db.Boards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == boardId, cancellationToken);
        if (board is null)
            return BoardOutcome.NotFound;

        return board.OwnerId == actorId ? null : BoardOutcome.Forbidden;
    }

    private static string OwnerMessage(BoardOutcome outcome)
        => outcome == BoardOutcome.Forbidden
            ? "Это может сделать только владелец доски."
            : "Доска не найдена.";
}
