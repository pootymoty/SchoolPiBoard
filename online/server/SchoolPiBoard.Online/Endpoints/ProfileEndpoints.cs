using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SchoolPiBoard.Online.Services;

namespace SchoolPiBoard.Online.Endpoints;

/// <summary>Настройки профиля: имя, пароль, удаление учётной записи.</summary>
public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app)
    {
        var profile = app.MapGroup("/profile").RequireCors(AuthEndpoints.CorsPolicy);

        profile.MapPatch("/", async (
            [FromBody] ChangeNameRequest request,
            ClaimsPrincipal principal,
            [FromServices] AccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await accounts.ChangeNameAsync(userId.Value, request.LastName, request.FirstName, cancellationToken);

            return result.IsOk
                ? Results.Ok(new { user = result.User!.ToDto() })
                : Answers.BadRequest(result.Message);
        })
        .RequireAuthorization();

        profile.MapPost("/password", async (
            [FromBody] ChangePasswordRequest request,
            ClaimsPrincipal principal,
            [FromServices] AccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await accounts.ChangePasswordAsync(
                userId.Value, request.CurrentPassword, request.NewPassword, request.ConfirmPassword, cancellationToken);

            return result.Outcome switch
            {
                AccountOutcome.Ok => Results.Ok(new { ok = true, message = "Пароль изменён." }),
                AccountOutcome.InvalidCredentials =>
                    Answers.Error(StatusCodes.Status401Unauthorized, "invalid_credentials", result.Message),
                _ => Answers.BadRequest(result.Message)
            };
        })
        .RequireAuthorization()
        .RequireRateLimiting(AuthEndpoints.AuthRateLimit);

        // Удаление в два шага: здесь только письмо со ссылкой.
        profile.MapPost("/delete-request", async (
            ClaimsPrincipal principal,
            [FromServices] AccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await accounts.RequestDeletionAsync(userId.Value, cancellationToken);

            return result.IsOk
                ? Results.Ok(new
                {
                    ok = true,
                    message = "Мы отправили письмо со ссылкой. Подтвердите удаление по ней."
                })
                : Answers.BadRequest(result.Message);
        })
        .RequireAuthorization()
        .RequireRateLimiting(AuthEndpoints.AuthRateLimit);

        // Второй шаг. Авторизация не нужна: ссылка из письма и есть
        // подтверждение — человек мог открыть её в другом браузере.
        profile.MapPost("/delete-confirm", async (
            [FromBody] ConfirmRequest request,
            [FromServices] AccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var result = await accounts.ConfirmDeletionAsync(request.Token, cancellationToken);

            return result.Outcome switch
            {
                AccountOutcome.Ok => Results.Ok(new
                {
                    ok = true,
                    message = "Учётная запись удалена вместе с досками и подпиской."
                }),
                AccountOutcome.NotFound => Answers.NotFound(result.Message),
                _ => Answers.Error(StatusCodes.Status410Gone, "link_expired", result.Message)
            };
        })
        .RequireRateLimiting(AuthEndpoints.AuthRateLimit);
    }
}
