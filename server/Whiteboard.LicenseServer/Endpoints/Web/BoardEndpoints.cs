using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Whiteboard.LicenseServer.Data;
using Whiteboard.LicenseServer.Services;

namespace Whiteboard.LicenseServer.Endpoints;

public static class BoardEndpoints
{
    public static void MapBoardEndpoints(this WebApplication app)
    {
        var boards = app.MapGroup("/boards")
            .RequireCors(AuthEndpoints.CorsPolicy)
            .RequireAuthorization();

        boards.MapGet("/", async (
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var list = await service.ListAsync(userId.Value, cancellationToken);
            return Results.Ok(list.Select(x => x.Board.ToDto(x.Role, x.MemberCount)));
        });

        boards.MapPost("/", async (
            [FromBody] CreateBoardRequest request,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            // Подписка нужна тому, кто заводит доски. Приглашённым участникам
            // она не нужна — иначе учитель не смог бы позвать класс.
            var subscription = await auth.GetSubscriptionAsync(userId.Value, cancellationToken);
            if (subscription is null || !subscription.IsUsable(DateTime.UtcNow))
            {
                return Results.Json(new
                {
                    error = "subscription_required",
                    message = "Пробный период закончился. Оформите подписку, чтобы создавать доски."
                }, statusCode: StatusCodes.Status402PaymentRequired);
            }

            var result = await service.CreateAsync(userId.Value, request.Name, cancellationToken);
            return Results.Ok(result.Value!.ToDto(BoardRole.Owner, 1));
        });

        boards.MapGet("/{boardId:guid}", async (
            Guid boardId,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var role = await service.GetRoleAsync(boardId, userId.Value, cancellationToken);
            var board = await service.GetAsync(boardId, userId.Value, cancellationToken);

            if (!board.IsOk || board.Value is null || role is null)
                return NotFound(board.Message);

            var members = await service.ListMembersAsync(boardId, userId.Value, cancellationToken);
            return Results.Ok(board.Value.ToDto(role.Value, members.Value?.Count ?? 1));
        });

        boards.MapDelete("/{boardId:guid}", async (
            Guid boardId,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var outcome = await service.DeleteAsync(boardId, userId.Value, cancellationToken);

            return outcome switch
            {
                BoardOutcome.Ok => Results.Ok(new { ok = true }),
                BoardOutcome.Forbidden => Forbidden("Удалить доску может только её владелец."),
                _ => NotFound("Доска не найдена.")
            };
        });

        boards.MapGet("/{boardId:guid}/members", async (
            Guid boardId,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.ListMembersAsync(boardId, userId.Value, cancellationToken);
            if (!result.IsOk || result.Value is null)
                return NotFound(result.Message);

            return Results.Ok(result.Value.Select(x => x.ToDto()));
        });

        boards.MapPost("/{boardId:guid}/members", async (
            Guid boardId,
            [FromBody] AddMemberRequest request,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.AddMemberAsync(boardId, userId.Value, request.Email, request.Role, cancellationToken);
            return Answer(result, member => Results.Ok(member.ToDto()));
        });

        boards.MapPatch("/{boardId:guid}/members/{memberUserId:guid}", async (
            Guid boardId,
            Guid memberUserId,
            [FromBody] ChangeRoleRequest request,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.ChangeRoleAsync(boardId, userId.Value, memberUserId, request.Role, cancellationToken);
            return Answer(result, member => Results.Ok(member.ToDto()));
        });

        boards.MapDelete("/{boardId:guid}/members/{memberUserId:guid}", async (
            Guid boardId,
            Guid memberUserId,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.RemoveMemberAsync(boardId, userId.Value, memberUserId, cancellationToken);
            return Answer(result, _ => Results.Ok(new { ok = true }));
        });
    }

    /// <summary>Общий перевод результата операции в ответ HTTP.</summary>
    private static IResult Answer<T>(BoardResult<T> result, Func<T, IResult> onSuccess)
        => result.Outcome switch
        {
            BoardOutcome.Ok => onSuccess(result.Value!),
            BoardOutcome.Forbidden => Forbidden(result.Message),
            BoardOutcome.UserNotFound => Results.Json(new { error = "user_not_found", message = result.Message },
                statusCode: StatusCodes.Status404NotFound),
            BoardOutcome.BadRequest => Results.Json(new { error = "bad_request", message = result.Message },
                statusCode: StatusCodes.Status400BadRequest),
            _ => NotFound(result.Message)
        };

    private static IResult NotFound(string? message)
        => Results.Json(new { error = "not_found", message = message ?? "Не найдено." },
            statusCode: StatusCodes.Status404NotFound);

    private static IResult Forbidden(string? message)
        => Results.Json(new { error = "forbidden", message = message ?? "Недостаточно прав." },
            statusCode: StatusCodes.Status403Forbidden);
}
