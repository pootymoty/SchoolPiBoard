using Whiteboard.LicenseServer.Data;

namespace Whiteboard.LicenseServer.Endpoints;

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record CreateBoardRequest(string? Name);

public sealed record AddMemberRequest(string? Email, string? Role);

public sealed record ChangeRoleRequest(string? Role);

public sealed record UserDto(Guid Id, string Email, string Name);

public sealed record SubscriptionDto(string Plan, string Status, bool Active, DateTime? TrialEndsAt, DateTime? CurrentPeriodEnd);

public sealed record BoardDto(
    Guid Id,
    string Name,
    string Role,
    bool CanEdit,
    bool CanManage,
    int MemberCount,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record MemberDto(Guid UserId, string Email, string Name, string Role, DateTime InvitedAt);

/// <summary>Перевод сущностей в то, что уходит в браузер.</summary>
public static class WebMapping
{
    public static UserDto ToDto(this User user)
        => new(user.Id, user.Email, string.IsNullOrWhiteSpace(user.DisplayName)
            ? NameFromEmail(user.Email)
            : user.DisplayName);

    public static SubscriptionDto? ToDto(this Subscription? subscription)
        => subscription is null
            ? null
            : new SubscriptionDto(
                subscription.Plan,
                subscription.Status,
                subscription.IsUsable(DateTime.UtcNow),
                subscription.TrialEndsAt,
                subscription.CurrentPeriodEnd);

    public static BoardDto ToDto(this Board board, BoardRole role, int memberCount)
        => new(
            board.Id,
            board.Name,
            BoardRoles.ToName(role),
            BoardRoles.CanEdit(role),
            BoardRoles.CanManage(role),
            memberCount,
            board.CreatedAt,
            board.ModifiedAt);

    public static MemberDto ToDto(this BoardMember member)
        => new(
            member.UserId,
            member.User?.Email ?? string.Empty,
            string.IsNullOrWhiteSpace(member.User?.DisplayName)
                ? NameFromEmail(member.User?.Email)
                : member.User!.DisplayName,
            member.Role,
            member.InvitedAt);

    public static string NameFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "Участник";

        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}
