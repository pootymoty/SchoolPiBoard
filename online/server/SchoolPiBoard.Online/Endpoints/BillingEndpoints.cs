using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SchoolPiBoard.Online.Configuration;
using SchoolPiBoard.Online.Services;

namespace SchoolPiBoard.Online.Endpoints;

/// <summary>
/// Подписка: тарифы, пробный период, оплата и управление.
///
/// Оплата идёт через Робокассу — она доступна самозанятому продавцу
/// в России, в отличие от Stripe. Об оплате сервис узнаёт не от браузера,
/// а от платёжной системы по ResultURL.
/// </summary>
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        var billing = app.MapGroup("/billing").RequireCors(AuthEndpoints.CorsPolicy);

        billing.MapGet("/plans", () => Results.Ok(new
        {
            trialDays = SubscriptionPlans.TrialDays,
            plans = SubscriptionPlans.All.Select(plan => plan.ToDto())
        }));

        billing.MapGet("/status", async (
            ClaimsPrincipal principal,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var subscription = await subscriptions.GetAsync(userId.Value, cancellationToken);
            return Results.Ok(new { subscription = subscription.ToDto() });
        })
        .RequireAuthorization();

        billing.MapPost("/trial", async (
            ClaimsPrincipal principal,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await subscriptions.StartTrialAsync(userId.Value, cancellationToken);

            return result.Outcome switch
            {
                BillingOutcome.Ok => Results.Ok(new { subscription = result.Subscription.ToDto() }),
                BillingOutcome.TrialUsed => Answers.Error(StatusCodes.Status409Conflict, "trial_used", result.Message),
                BillingOutcome.NotFound => Answers.NotFound(result.Message),
                _ => Answers.BadRequest(result.Message)
            };
        })
        .RequireAuthorization();

        billing.MapPost("/checkout", async (
            [FromBody] CheckoutRequest request,
            ClaimsPrincipal principal,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await subscriptions.CreateCheckoutAsync(userId.Value, request.PlanDays, cancellationToken);

            return result.Outcome switch
            {
                BillingOutcome.Ok => Results.Ok(new { paymentUrl = result.PaymentUrl }),
                BillingOutcome.NotConfigured =>
                    Answers.Error(StatusCodes.Status503ServiceUnavailable, "payments_disabled", result.Message),
                BillingOutcome.NotFound => Answers.NotFound(result.Message),
                _ => Answers.BadRequest(result.Message)
            };
        })
        .RequireAuthorization()
        .RequireRateLimiting(AuthEndpoints.AuthRateLimit);

        billing.MapPost("/auto-renew", async (
            [FromBody] AutoRenewRequest request,
            ClaimsPrincipal principal,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await subscriptions.SetAutoRenewAsync(userId.Value, request.Enabled, cancellationToken);

            return result.IsOk
                ? Results.Ok(new { subscription = result.Subscription.ToDto() })
                : Answers.NotFound(result.Message);
        })
        .RequireAuthorization();

        billing.MapPost("/cancel", async (
            ClaimsPrincipal principal,
            [FromServices] SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.UserId();
            if (userId is null)
                return Results.Unauthorized();

            var result = await subscriptions.CancelAsync(userId.Value, cancellationToken);

            return result.IsOk
                ? Results.Ok(new
                {
                    subscription = result.Subscription.ToDto(),
                    message = "Подписка отменена. Доступ сохраняется до конца оплаченного срока."
                })
                : Answers.NotFound(result.Message);
        })
        .RequireAuthorization();

        // ResultURL Робокассы: не браузер, а сама платёжная система.
        // Отвечать нужно строкой OK{InvId}, иначе уведомление повторится.
        app.MapMethods("/billing/robokassa/result", new[] { "POST", "GET" }, async (
            HttpRequest httpRequest,
            [FromServices] RobokassaService robokassa,
            [FromServices] SubscriptionService subscriptions,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Robokassa");

            IFormCollection? form = null;
            if (httpRequest.HasFormContentType)
                form = await httpRequest.ReadFormAsync(cancellationToken);

            string? Value(string name) => form is not null
                ? form[name].ToString()
                : httpRequest.Query[name].ToString();

            var outSum = Value("OutSum");
            var invoice = Value("InvId");
            var signature = Value("SignatureValue");

            if (!robokassa.VerifyResultSignature(outSum, invoice, signature))
            {
                logger.LogWarning("Уведомление об оплате отклонено: подпись не сходится.");
                return Results.Text("bad sign", "text/plain", null, StatusCodes.Status400BadRequest);
            }

            if (!long.TryParse(invoice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var invoiceId))
                return Results.Text("bad invoice", "text/plain", null, StatusCodes.Status400BadRequest);

            var result = await subscriptions.ApplyPaymentAsync(invoiceId, cancellationToken);
            if (!result.IsOk)
            {
                logger.LogError("Оплата по неизвестному счёту {InvoiceId}.", invoiceId);
                return Results.Text("unknown invoice", "text/plain", null, StatusCodes.Status400BadRequest);
            }

            return Results.Text($"OK{invoiceId}", "text/plain");
        });
    }
}
