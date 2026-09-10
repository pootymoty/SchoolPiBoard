using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SchoolPi.Tariffs.Configuration;
using SchoolPi.Tariffs.Data;
using SchoolPi.Tariffs.Services;

namespace SchoolPi.Tariffs.Endpoints;

public sealed record CheckoutRequest(int ExternalUserId, string Email, string Plan, bool AutoRenew, string? Consent);
public sealed record AutoRenewCancelRequest(int ExternalUserId);

/// <summary>Сообщение сервера ключей об оплате — тот же вид, что у доски (PaidCallback в BillingEndpoints.cs).</summary>
public sealed record PaidCallback(string? InvoiceId, long UserId, string? PlanCode, int Days, decimal Amount,
    bool AutoRenew, DateTime? PaidAt);

public static class TariffEndpoints
{
    public static void MapTariffEndpoints(this WebApplication app, TariffOptions options)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/plans", () => Results.Ok(new
        {
            plans = TariffPlans.All.Select(plan => new { plan.Key, plan.Title, plan.Price }),
            periodDays = TariffPlans.PeriodDays
        }));

        var api = app.MapGroup("").AddEndpointFilter(async (context, next) =>
        {
            var provided = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
            if (!FixedTimeEquals(provided, options.ApiKey))
                return Results.Unauthorized();

            return await next(context);
        });

        api.MapGet("/status/{externalUserId:int}", async (
            int externalUserId, SubscriptionService subscriptions, CancellationToken ct) =>
        {
            var current = await subscriptions.CurrentAsync(externalUserId, ct);
            var upcoming = await subscriptions.UpcomingAsync(externalUserId, ct);

            return Results.Ok(new
            {
                subscription = current is null ? null : Describe(current),
                upcoming = upcoming.Select(Describe),
            });
        });

        api.MapPost("/checkout", async (
            [FromBody] CheckoutRequest request, SubscriptionService subscriptions, CancellationToken ct) =>
        {
            var result = await subscriptions.CreateCheckoutAsync(
                request.ExternalUserId, request.Email, request.Plan, request.AutoRenew, request.Consent, ct);

            return result.Outcome switch
            {
                BillingOutcome.Ok => Results.Ok(new { paymentUrl = result.PaymentUrl }),
                BillingOutcome.NotConfigured => Results.Json(new { error = result.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status400BadRequest),
            };
        });

        // Отключить автопродление — включить обратно можно только новой
        // покупкой: это и есть согласие на следующее списание.
        api.MapPost("/auto-renew/cancel", async (
            [FromBody] AutoRenewCancelRequest request, SubscriptionService subscriptions, CancellationToken ct) =>
        {
            var changed = await subscriptions.CancelAutoRenewAsync(request.ExternalUserId, ct);
            return changed
                ? Results.Ok(new { autoRenew = false })
                : Results.Json(new { error = "Автопродления нет или оно уже отключено." },
                    statusCode: StatusCodes.Status400BadRequest);
        });

        // Сообщение об оплате от сервера ключей. Без X-Api-Key: это
        // разговор двух своих служб, подписанный общим секретом с
        // сервером ключей (TARIFFS_SHARED_SECRET), а не ключом сайта.
        app.MapPost("/callback", async (
            HttpRequest http, LicenseServerClient licenseServer, SubscriptionService subscriptions,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("Callback");

            using var reader = new StreamReader(http.Body);
            var body = await reader.ReadToEndAsync(ct);

            var timestamp = http.Headers[LicenseServerClient.TimestampHeader].ToString();
            var signature = http.Headers[LicenseServerClient.SignatureHeader].ToString();

            if (!licenseServer.Verify(timestamp, signature, body))
            {
                logger.LogWarning("Сообщение об оплате отклонено: подпись не сходится.");
                return Results.Json(new { message = "Подпись не сходится." }, statusCode: 403);
            }

            PaidCallback? paid;
            try
            {
                paid = JsonSerializer.Deserialize<PaidCallback>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { message = "Сообщение не разобрано." });
            }

            if (paid is null || string.IsNullOrWhiteSpace(paid.InvoiceId) || paid.UserId <= 0
                || !long.TryParse(paid.InvoiceId, out var invoiceId))
            {
                return Results.BadRequest(new { message = "Сообщение не разобрано." });
            }

            var result = await subscriptions.ApplyPaymentAsync(invoiceId, ct);
            if (!result.IsOk)
            {
                logger.LogError("Оплата по неизвестному счёту {InvoiceId}.", invoiceId);
                return Results.BadRequest(new { message = result.Message });
            }

            return Results.Ok(new { ok = true });
        });
    }

    private static object Describe(Subscription subscription) => new
    {
        plan = subscription.Plan,
        startsAt = subscription.StartsAt,
        endsAt = subscription.EndsAt,
        autoRenew = subscription.AutoRenew,
    };

    private static bool FixedTimeEquals(string a, string b)
    {
        var hashA = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(a));
        var hashB = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(b));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}
