using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SchoolPiBoard.Online.Services;

namespace SchoolPiBoard.Online.Endpoints;

public static class AuthEndpoints
{
    /// <summary>Политика CORS для веб-приложения.</summary>
    public const string CorsPolicy = "app";

    /// <summary>Ограничитель для входа и регистрации — защита от перебора.</summary>
    public const string AuthRateLimit = "auth";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/auth").RequireCors(CorsPolicy);

        auth.MapPost("/register", async (
            [FromBody] RegisterRequest request,
            HttpContext http,
            [FromServices] AccountService accounts,
            [FromServices] ICaptchaVerifier captcha,
            CancellationToken cancellationToken) =>
        {
            // Капчу проверяем до всего остального: она и нужна затем,
            // чтобы дальше не пускать роботов.
            var ip = http.Connection.RemoteIpAddress?.ToString();
            if (!await captcha.VerifyAsync(request.CaptchaToken, ip, cancellationToken))
                return Answers.BadRequest("Не пройдена проверка «я не робот». Попробуйте ещё раз.");

            var result = await accounts.RegisterAsync(
                request.LastName, request.FirstName, request.BirthDate,
                request.Email, request.Password, request.PasswordConfirm, cancellationToken);

            return result.Outcome switch
            {
                AccountOutcome.Ok => Results.Ok(new
                {
                    status = "confirm_sent",
                    message = "Мы отправили письмо со ссылкой подтверждения. Ссылка действует час."
                }),
                AccountOutcome.EmailTaken => Answers.Error(StatusCodes.Status409Conflict, "email_taken", result.Message),
                _ => Answers.BadRequest(result.Message)
            };
        })
        .RequireRateLimiting(AuthRateLimit);

        auth.MapPost("/confirm", async (
            [FromBody] ConfirmRequest request,
            [FromServices] AccountService accounts,
            CancellationToken cancellationToken) =>
        {
            var result = await accounts.ConfirmRegistrationAsync(request.Token, cancellationToken);

            return result.Outcome switch
            {
                AccountOutcome.Ok => Results.Ok(new
                {
                    status = "confirmed",
                    message = "Почта подтверждена. Теперь можно войти."
                }),
                AccountOutcome.EmailTaken => Answers.Error(StatusCodes.Status409Conflict, "email_taken", result.Message),
                // 410: ссылка была настоящей, но её время прошло.
                _ => Answers.Error(StatusCodes.Status410Gone, "link_expired", result.Message)
            };
        })
        .RequireRateLimiting(AuthRateLimit);

        auth.MapPost("/login", async (
            [FromBody] LoginRequest request,
            [FromServices] AccountService accounts,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var result = await accounts.LoginAsync(request.Email, request.Password, cancellationToken);

            if (result.Outcome == AccountOutcome.NotConfirmed)
                return Answers.Error(StatusCodes.Status403Forbidden, "not_confirmed", result.Message);

            if (!result.IsOk || result.User is null)
                return Answers.Error(StatusCodes.Status401Unauthorized, "invalid_credentials", result.Message);

            var subscription = await subscriptions.GetAsync(result.User.Id, cancellationToken);

            return Results.Ok(new
            {
                token = result.Token,
                user = result.User.ToDto(),
                subscription = subscription.ToDto()
            });
        })
        .RequireRateLimiting(AuthRateLimit);

        auth.MapGet("/me", async (
            ClaimsPrincipal principal,
            [FromServices] AccountService accounts,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var user = await accounts.FindAsync(userId.Value, cancellationToken);
            if (user is null)
                return Results.Unauthorized();

            var subscription = await subscriptions.GetAsync(user.Id, cancellationToken);
            return Results.Ok(new { user = user.ToDto(), subscription = subscription.ToDto() });
        })
        .RequireAuthorization();
    }
}
