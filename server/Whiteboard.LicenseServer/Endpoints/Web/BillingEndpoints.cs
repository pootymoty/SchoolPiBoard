using System.Security.Claims;
using Whiteboard.LicenseServer.Services;

namespace Whiteboard.LicenseServer.Endpoints;

/// <summary>
/// Подписка на веб-версию.
///
/// Платёжная система для подписок пока не выбрана: в ТЗ указан Stripe, но
/// в Фазе 1 выяснилось, что продавцу со статусом самозанятого он недоступен
/// (см. docs/payment-legal-notes.md). Поэтому здесь пока только то, что
/// от провайдера не зависит: состояние подписки и пробный период, который
/// заводится при регистрации. Точка подключения оплаты — одна, и она здесь.
/// </summary>
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        var billing = app.MapGroup("/billing")
            .RequireCors(AuthEndpoints.CorsPolicy);

        billing.MapGet("/status", async (
            ClaimsPrincipal principal,
            AuthService auth,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var subscription = await auth.GetSubscriptionAsync(userId.Value, cancellationToken);
            return Results.Ok(new { subscription = subscription.ToDto() });
        })
        .RequireAuthorization();

        billing.MapPost("/create-checkout-session", () =>
            Results.Json(new
            {
                error = "billing_not_configured",
                message = "Оплата подписки ещё не подключена. Напишите нам — продлим доступ вручную."
            }, statusCode: StatusCodes.Status503ServiceUnavailable))
        .RequireAuthorization();

        // Вебхук платёжной системы. Открыт без авторизации (её заменяет
        // проверка подписи), но пока принимать нечего.
        billing.MapPost("/webhook", () => Results.Ok(new { status = "ignored" }));
    }
}
