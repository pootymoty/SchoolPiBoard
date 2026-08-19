using Microsoft.AspNetCore.Mvc;
using Whiteboard.LicenseServer.Services;

namespace Whiteboard.LicenseServer.Endpoints;

public static class AuthEndpoints
{
    /// <summary>Политика CORS для веб-приложения: ему нужны заголовки, методы и WebSocket.</summary>
    public const string CorsPolicy = "app";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/register", async (
            [FromBody] RegisterRequest request,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.RegisterAsync(request.Email, request.Password, request.DisplayName, cancellationToken);

            return result.Outcome switch
            {
                AuthOutcome.Ok => Results.Ok(new
                {
                    token = result.Token,
                    user = result.User!.ToDto(),
                    subscription = (await auth.GetSubscriptionAsync(result.User.Id, cancellationToken)).ToDto()
                }),

                AuthOutcome.EmailTaken => Results.Json(new { error = "email_taken", message = result.Message },
                    statusCode: StatusCodes.Status409Conflict),

                _ => Results.Json(new { error = "bad_request", message = result.Message },
                    statusCode: StatusCodes.Status400BadRequest)
            };
        })
        .RequireCors(CorsPolicy)
        .RequireRateLimiting(LicenseEndpoints.ActivatePolicy);

        app.MapPost("/auth/login", async (
            [FromBody] LoginRequest request,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            var result = await auth.LoginAsync(request.Email, request.Password, cancellationToken);

            if (result.Outcome != AuthOutcome.Ok || result.User is null)
            {
                return Results.Json(new { error = "invalid_credentials", message = result.Message },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(new
            {
                token = result.Token,
                user = result.User.ToDto(),
                subscription = (await auth.GetSubscriptionAsync(result.User.Id, cancellationToken)).ToDto()
            });
        })
        .RequireCors(CorsPolicy)
        .RequireRateLimiting(LicenseEndpoints.ActivatePolicy);

        app.MapGet("/auth/me", async (
            System.Security.Claims.ClaimsPrincipal principal,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var user = await auth.FindAsync(userId.Value, cancellationToken);
            if (user is null)
            {
                // Токен ещё живой, а пользователя уже нет — просим войти заново.
                return Results.Unauthorized();
            }

            return Results.Ok(new
            {
                user = user.ToDto(),
                subscription = (await auth.GetSubscriptionAsync(user.Id, cancellationToken)).ToDto()
            });
        })
        .RequireCors(CorsPolicy)
        .RequireAuthorization();
    }
}
