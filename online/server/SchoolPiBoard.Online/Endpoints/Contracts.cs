using SchoolPiBoard.Online.Configuration;
using SchoolPiBoard.Online.Data;
using SchoolPiBoard.Online.Services;

namespace SchoolPiBoard.Online.Endpoints;

public sealed record RegisterRequest(
    string? LastName,
    string? FirstName,
    DateOnly? BirthDate,
    string? Email,
    string? Password,
    string? PasswordConfirm,
    string? CaptchaToken);

public sealed record ConfirmRequest(string? Token);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record ChangeNameRequest(string? LastName, string? FirstName);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword, string? ConfirmPassword);
public sealed record CreateBoardRequest(string? Name);
public sealed record AddMemberRequest(string? Email, string? Role);
public sealed record ChangeRoleRequest(string? Role);
public sealed record CreateInviteRequest(string? Role, int? LifetimeDays, int? EditDays);
public sealed record CheckoutRequest(int PlanDays);
public sealed record AutoRenewRequest(bool Enabled);

public sealed record UserDto(Guid Id, string Email, string LastName, string FirstName, DateOnly BirthDate, bool TrialUsed);

public sealed record SubscriptionDto(string Kind, int PlanDays, string Status, bool Active, DateTime ExpiresAt, bool AutoRenew);

public sealed record PlanDto(int Days, decimal Price, string Title);

public sealed record BoardDto(
    Guid Id,
    string Name,
    string Role,
    bool CanEdit,
    bool CanManage,
    bool Invited,
    int MemberCount,
    DateTime? EditUntil,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public sealed record MemberDto(Guid UserId, string Email, string Name, string Role, bool ViaLink, DateTime? EditUntil, DateTime InvitedAt);

public sealed record InviteDto(Guid Id, string Role, DateTime CreatedAt, DateTime ExpiresAt, int Uses, int? EditDays, string? Url);

public static class Mapping
{
    public static UserDto ToDto(this User user)
        => new(user.Id, user.Email, user.LastName, user.FirstName, user.BirthDate, user.TrialUsedAt is not null);

    public static SubscriptionDto? ToDto(this Subscription? subscription)
        => subscription is null
            ? null
            : new SubscriptionDto(
                subscription.Kind,
                subscription.PlanDays,
                subscription.Status,
                subscription.IsActive(DateTime.UtcNow),
                subscription.ExpiresAt,
                subscription.AutoRenew);

    public static PlanDto ToDto(this SubscriptionPlan plan) => new(plan.Days, plan.Price, plan.Title);

    public static BoardDto ToDto(this BoardListItem item)
        => new(
            item.Board.Id,
            item.Board.Name,
            BoardRoles.ToName(item.Role),
            BoardRoles.CanEdit(item.Role),
            BoardRoles.CanManage(item.Role),
            item.Invited,
            item.MemberCount,
            item.EditUntil,
            item.Board.CreatedAt,
            item.Board.ModifiedAt);

    public static BoardDto ToDto(this Board board, BoardRole role, bool invited, int memberCount, DateTime? editUntil = null)
        => new(
            board.Id,
            board.Name,
            BoardRoles.ToName(role),
            BoardRoles.CanEdit(role),
            BoardRoles.CanManage(role),
            invited,
            memberCount,
            editUntil,
            board.CreatedAt,
            board.ModifiedAt);

    public static MemberDto ToDto(this BoardMember member)
        => new(
            member.UserId,
            member.User?.Email ?? string.Empty,
            member.User?.FullName ?? "Участник",
            BoardRoles.ToName(member.EffectiveRole(DateTime.UtcNow)),
            member.ViaLink,
            member.EditUntil,
            member.InvitedAt);

    public static InviteDto ToDto(this BoardInvite invite, string? url = null)
        => new(invite.Id, invite.Role, invite.CreatedAt, invite.ExpiresAt, invite.Uses, invite.EditDays, url);
}

/// <summary>Единый формат ответов об ошибке: код для кода, message для человека.</summary>
public static class Answers
{
    public static IResult Error(int statusCode, string code, string? message)
        => Results.Json(new { error = code, message = message ?? "Что-то пошло не так." }, statusCode: statusCode);

    public static IResult BadRequest(string? message) => Error(StatusCodes.Status400BadRequest, "bad_request", message);

    public static IResult NotFound(string? message) => Error(StatusCodes.Status404NotFound, "not_found", message ?? "Не найдено.");

    public static IResult Forbidden(string? message) => Error(StatusCodes.Status403Forbidden, "forbidden", message ?? "Недостаточно прав.");
}
