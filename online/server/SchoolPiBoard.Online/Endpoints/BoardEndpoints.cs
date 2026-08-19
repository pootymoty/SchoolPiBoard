using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SchoolPiBoard.Online.Data;
using SchoolPiBoard.Online.Services;

namespace SchoolPiBoard.Online.Endpoints;

public static class BoardEndpoints
{
    public static void MapBoardEndpoints(this WebApplication app)
    {
        var boards = app.MapGroup("/boards")
            .RequireCors(AuthEndpoints.CorsPolicy)
            .RequireAuthorization();

        boards.MapGet("/", async (
            int? page,
            int? pageSize,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.ListAsync(
                userId.Value, page ?? 1, pageSize ?? BoardService.DefaultPageSize, cancellationToken);

            return Results.Ok(new
            {
                items = result.Items.Select(item => item.ToDto()),
                page = result.Page,
                pageSize = result.PageSize,
                total = result.Total
            });
        });

        boards.MapPost("/", async (
            [FromBody] CreateBoardRequest request,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            // Подписка нужна тому, кто заводит доски. Приглашённым — нет:
            // иначе учитель не смог бы позвать учеников.
            if (!await subscriptions.IsActiveAsync(userId.Value, cancellationToken))
            {
                return Answers.Error(StatusCodes.Status402PaymentRequired, "subscription_required",
                    "Чтобы создавать доски, нужна подписка или пробный период.");
            }

            var result = await service.CreateAsync(userId.Value, request.Name, cancellationToken);
            return Results.Ok(result.Value!.ToDto(BoardRole.Owner, invited: false, memberCount: 1));
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
                return Answers.NotFound(board.Message);

            var members = await service.ListMembersAsync(boardId, userId.Value, cancellationToken);

            return Results.Ok(board.Value.ToDto(
                role.Value,
                invited: board.Value.OwnerId != userId.Value,
                memberCount: members.Value?.Count ?? 1));
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
                BoardOutcome.Forbidden => Answers.Forbidden("Удалить доску может только её владелец."),
                _ => Answers.NotFound("Доска не найдена.")
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

            return result.IsOk && result.Value is not null
                ? Results.Ok(result.Value.Select(member => member.ToDto()))
                : Answers.NotFound(result.Message);
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

        // ---------- ссылки-приглашения ----------

        boards.MapGet("/{boardId:guid}/invites", async (
            Guid boardId,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.ListInvitesAsync(boardId, userId.Value, cancellationToken);

            // Саму ссылку показываем только один раз — при создании:
            // в базе лежит хеш, восстановить её оттуда нельзя.
            return result.IsOk && result.Value is not null
                ? Results.Ok(result.Value.Select(invite => invite.ToDto()))
                : Answer(result, _ => Results.Ok(Array.Empty<InviteDto>()));
        });

        boards.MapPost("/{boardId:guid}/invites", async (
            Guid boardId,
            [FromBody] CreateInviteRequest request,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.CreateInviteAsync(
                boardId, userId.Value, request.Role, request.LifetimeDays, request.EditDays, cancellationToken);

            return Answer(result, link => Results.Ok(link.Invite.ToDto(link.Url)));
        });

        boards.MapDelete("/{boardId:guid}/invites/{inviteId:guid}", async (
            Guid boardId,
            Guid inviteId,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var outcome = await service.RevokeInviteAsync(boardId, userId.Value, inviteId, cancellationToken);

            return outcome switch
            {
                BoardOutcome.Ok => Results.Ok(new { ok = true }),
                BoardOutcome.Forbidden => Answers.Forbidden("Это может сделать только владелец доски."),
                _ => Answers.NotFound("Ссылка не найдена.")
            };
        });

        // Вход по ссылке. Отдельная группа: просмотр доступен и без входа,
        // чтобы человек понимал, куда его зовут, прежде чем регистрироваться.
        var invites = app.MapGroup("/invites").RequireCors(AuthEndpoints.CorsPolicy);

        invites.MapGet("/{token}", async (
            string token,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.PeekInviteAsync(token, cancellationToken);

            return result.IsOk && result.Value is not null
                ? Results.Ok(new { boardName = result.Value.Name })
                : Answers.Error(StatusCodes.Status410Gone, "invite_expired", result.Message);
        });

        invites.MapPost("/{token}/join", async (
            string token,
            ClaimsPrincipal principal,
            [FromServices] BoardService service,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await service.JoinByInviteAsync(token, userId.Value, cancellationToken);

            if (!result.IsOk || result.Value is null)
                return Answers.Error(StatusCodes.Status410Gone, "invite_expired", result.Message);

            var role = await service.GetRoleAsync(result.Value.Id, userId.Value, cancellationToken) ?? BoardRole.Viewer;
            return Results.Ok(result.Value.ToDto(role, invited: true, memberCount: 0));
        })
        .RequireAuthorization();
    }

    private static IResult Answer<T>(BoardResult<T> result, Func<T, IResult> onSuccess)
        => result.Outcome switch
        {
            BoardOutcome.Ok => onSuccess(result.Value!),
            BoardOutcome.Forbidden => Answers.Forbidden(result.Message),
            BoardOutcome.UserNotFound => Answers.Error(StatusCodes.Status404NotFound, "user_not_found", result.Message),
            BoardOutcome.BadRequest => Answers.BadRequest(result.Message),
            BoardOutcome.InviteExpired => Answers.Error(StatusCodes.Status410Gone, "invite_expired", result.Message),
            _ => Answers.NotFound(result.Message)
        };
}
