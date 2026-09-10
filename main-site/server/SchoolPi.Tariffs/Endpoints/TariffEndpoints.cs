using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SchoolPi.Tariffs.Configuration;
using SchoolPi.Tariffs.Services;

namespace SchoolPi.Tariffs.Endpoints;

public sealed record CheckoutRequest(int ExternalUserId, string Plan, string? Email);

public static class TariffEndpoints
{
    public static void MapTariffEndpoints(this WebApplication app, TariffOptions options)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/plans", () => Results.Ok(new
        {
            plans = TariffPlans.All.Select(plan => new { plan.Key, plan.Title, plan.Price })
        }));

        var api = app.MapGroup("").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var provided = http.Request.Headers["X-Api-Key"].ToString();

            // Сравнение постоянного времени: сервис маленький и стоит за
            // внутренним адресом, но угадывать секрет по времени ответа не
            // должно быть возможно даже здесь.
            if (!FixedTimeEquals(provided, options.ApiKey))
                return Results.Unauthorized();

            return await next(context);
        });

        api.MapGet("/status/{externalUserId:int}", async (
            int externalUserId,
            SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var subscription = await subscriptions.GetAsync(externalUserId, cancellationToken);
            if (subscription is null)
                return Results.Ok(new { subscription = (object?)null });

            return Results.Ok(new
            {
                subscription = new
                {
                    plan = subscription.Plan,
                    startedAt = subscription.StartedAt,
                    expiresAt = subscription.ExpiresAt,
                    isActive = subscription.IsActive(DateTime.UtcNow),
                }
            });
        });

        api.MapPost("/checkout", async (
            [FromBody] CheckoutRequest request,
            SubscriptionService subscriptions,
            CancellationToken cancellationToken) =>
        {
            var result = await subscriptions.CreateCheckoutAsync(
                request.ExternalUserId, request.Plan, request.Email, cancellationToken);

            return result.Outcome switch
            {
                BillingOutcome.Ok => Results.Ok(new { paymentUrl = result.PaymentUrl }),
                BillingOutcome.NotConfigured => Results.Json(new { error = result.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status400BadRequest),
            };
        });

        // ResultURL Робокассы: приходит от самой платёжной системы, не от
        // браузера покупателя — вот почему это единственный внешний адрес
        // без заголовка X-Api-Key. Подлинность проверяется подписью с
        // Password2, подделать которую без него нельзя. Отвечать нужно
        // строкой OK{InvId}, иначе уведомление придёт снова.
        app.MapMethods("/robokassa/result", new[] { "POST", "GET" }, async (
            HttpRequest httpRequest,
            RobokassaService robokassa,
            SubscriptionService subscriptions,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Robokassa");

            Microsoft.AspNetCore.Http.IFormCollection? form = null;
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

    private static bool FixedTimeEquals(string a, string b)
    {
        // Сравниваются хэши фиксированной длины, а не сами строки: тогда
        // сравнение постоянного времени работает и когда длины входа не
        // совпадают, без отдельной ветки, которая сама могла бы стать
        // источником различия по времени.
        var hashA = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(a));
        var hashB = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(b));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}
