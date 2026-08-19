using System.Text;
using Microsoft.AspNetCore.Mvc;
using Whiteboard.LicenseServer.Configuration;
using Whiteboard.LicenseServer.Services;

namespace Whiteboard.LicenseServer.Endpoints;

public static class StripeWebhookEndpoints
{
    public static void MapStripeWebhook(this WebApplication app)
    {
        app.MapPost("/webhook/stripe", async (
            HttpRequest httpRequest,
            [FromServices] LicenseService licenses,
            [FromServices] IEmailSender emails,
            [FromServices] StripeOptions stripe,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("StripeWebhook");

            string payload;
            using (var reader = new StreamReader(httpRequest.Body, Encoding.UTF8))
                payload = await reader.ReadToEndAsync(cancellationToken);

            if (stripe.IsConfigured)
            {
                var signature = httpRequest.Headers["Stripe-Signature"].ToString();
                if (!StripeSignatureVerifier.Verify(payload, signature, stripe.WebhookSecret, DateTimeOffset.UtcNow))
                {
                    logger.LogWarning("Вебхук отклонён: подпись не сходится.");
                    return Results.Json(new { error = "invalid_signature" },
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }
            else
            {
                logger.LogWarning("Подпись вебхука не проверяется: не задан STRIPE_WEBHOOK_SECRET.");
            }

            if (!StripeEvent.TryParse(payload, out var stripeEvent) || stripeEvent is null)
            {
                logger.LogWarning("Вебхук отклонён: тело запроса не разобрано.");
                return Results.Json(new { error = "bad_payload" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!stripeEvent.IsPurchase)
            {
                // Событий у Stripe много, нас интересует только успешная оплата.
                // Отвечаем 200, иначе Stripe будет слать их повторно.
                return Results.Ok(new { status = "ignored", type = stripeEvent.Type });
            }

            if (string.IsNullOrWhiteSpace(stripeEvent.Email))
            {
                // Без почты письмо отправить некуда, а повторы не помогут.
                logger.LogError("В событии {Type} нет почты покупателя — ключ не выпущен.", stripeEvent.Type);
                return Results.Ok(new { status = "ignored", reason = "no_email" });
            }

            var license = await licenses.IssueForPaymentAsync(
                stripeEvent.Email.Trim(),
                StripeEvent.HashPaymentId(stripeEvent.PaymentId!),
                cancellationToken);

            if (license.EmailSentAt is null)
            {
                var sent = await emails.SendLicenseKeyAsync(license.Email, license.Key, cancellationToken);
                if (!sent)
                {
                    // Ключ уже в базе, поэтому повтор вебхука не создаст второй:
                    // он просто попробует отправить письмо ещё раз.
                    logger.LogError("Письмо с ключом не ушло, лицензия {LicenseId}.", license.Id);
                    return Results.Json(new { error = "email_failed" },
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                await licenses.MarkEmailSentAsync(license, cancellationToken);
            }

            return Results.Ok(new { status = "ok" });
        });
    }
}
