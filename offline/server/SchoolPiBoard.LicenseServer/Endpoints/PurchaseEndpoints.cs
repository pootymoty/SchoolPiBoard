using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SchoolPiBoard.LicenseServer.Configuration;
using SchoolPiBoard.LicenseServer.Data;
using SchoolPiBoard.LicenseServer.Services;

namespace SchoolPiBoard.LicenseServer.Endpoints;

public sealed record PurchaseRequest(string? Email);

/// <summary>
/// Покупка лицензии через Робокассу.
///
/// Страница сайта отправляет сюда только почту, дальше человек уходит на
/// форму Робокассы. Об оплате мы узнаём не от браузера, а от самой Робокассы
/// по ResultURL — браузеру в этом вопросе верить нельзя.
/// </summary>
public static class PurchaseEndpoints
{
    public const string CorsPolicy = "web";

    public static void MapPurchaseEndpoints(this WebApplication app)
    {
        app.MapPost("/purchase/start", async (
            HttpRequest httpRequest,
            [FromServices] PurchaseService purchases,
            [FromServices] RobokassaService robokassa,
            [FromServices] RobokassaOptions options,
            [FromServices] WebOptions web,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Purchase");

            // Со страницы сайта приходит обычная форма, из скриптов — JSON.
            var fromForm = httpRequest.HasFormContentType;
            string? email;

            if (fromForm)
            {
                var form = await httpRequest.ReadFormAsync(cancellationToken);
                email = form["email"].ToString();
            }
            else
            {
                try
                {
                    var body = await httpRequest.ReadFromJsonAsync<PurchaseRequest>(cancellationToken);
                    email = body?.Email;
                }
                catch (JsonException)
                {
                    email = null;
                }
            }

            if (!options.IsConfigured)
            {
                logger.LogError("Попытка оплаты при ненастроенной Робокассе.");
                return Answer(fromForm, StatusCodes.Status503ServiceUnavailable,
                    "payments_disabled",
                    "Оплата временно недоступна. Напишите нам, и мы поможем с покупкой.",
                    web);
            }

            var address = EmailAddress.Normalize(email);
            if (address is null)
            {
                return Answer(fromForm, StatusCodes.Status400BadRequest,
                    "bad_email",
                    "Проверьте адрес почты: именно на него придёт ключ.",
                    web);
            }

            var payment = await purchases.CreateAsync(
                address, robokassa.Amount, PurchaseService.ProviderRobokassa, cancellationToken);

            var paymentUrl = robokassa.BuildPaymentUrl(payment.InvoiceId, address);

            logger.LogInformation("Выставлен счёт {InvoiceId}.", payment.InvoiceId);

            return fromForm
                ? Results.Redirect(paymentUrl)
                : Results.Ok(new
                {
                    paymentUrl,
                    invoiceId = payment.InvoiceId,
                    amount = RobokassaService.FormatSum(robokassa.Amount)
                });
        })
        .RequireRateLimiting(LicenseEndpoints.ActivatePolicy)
        .RequireCors(CorsPolicy);

        // ResultURL Робокассы. Отвечать нужно строкой OK{InvId} — иначе
        // Робокасса считает уведомление недоставленным и повторяет его.
        app.MapMethods("/payment/robokassa/result", new[] { "POST", "GET" }, async (
            HttpRequest httpRequest,
            [FromServices] PurchaseService purchases,
            [FromServices] LicenseService licenses,
            [FromServices] RobokassaService robokassa,
            [FromServices] IEmailSender emails,
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

            var payment = await purchases.FindByInvoiceAsync(invoiceId, cancellationToken);
            if (payment is null)
            {
                logger.LogError("Оплата по неизвестному счёту {InvoiceId}.", invoiceId);
                return Results.Text("unknown invoice", "text/plain", null, StatusCodes.Status400BadRequest);
            }

            // Подпись уже подтвердила сумму, но расхождение с ценой стоит
            // увидеть в логе: значит, цену поменяли в одном месте из двух.
            if (!robokassa.IsExpectedAmount(outSum))
                logger.LogWarning("Счёт {InvoiceId} оплачен на сумму {Sum}, ожидалась другая.", invoiceId, outSum);

            // Повторное уведомление о том же счёте: лицензия уже выпущена.
            if (payment.Status == Payment.StatusPaid)
                return Results.Text($"OK{invoiceId}", "text/plain");

            var license = await licenses.IssueForPaymentAsync(
                payment.Email, PaymentHash.ForRobokassa(invoiceId), cancellationToken);

            if (license.EmailSentAt is null)
            {
                var sent = await emails.SendLicenseKeyAsync(license.Email, license.Key, cancellationToken);
                if (!sent)
                {
                    // Ключ уже в базе; счёт остаётся неоплаченным, поэтому
                    // повтор уведомления просто попробует отправить письмо снова.
                    logger.LogError("Письмо с ключом не ушло, счёт {InvoiceId}.", invoiceId);
                    return Results.Text("email failed", "text/plain", null,
                        StatusCodes.Status500InternalServerError);
                }

                await licenses.MarkEmailSentAsync(license, cancellationToken);
            }

            await purchases.MarkPaidAsync(payment, license.Id, cancellationToken);

            logger.LogInformation("Счёт {InvoiceId} оплачен, выпущена лицензия {LicenseId}.", invoiceId, license.Id);
            return Results.Text($"OK{invoiceId}", "text/plain");
        });
    }

    /// <summary>
    /// Форме отвечаем страницей, скрипту — JSON: пользователь, пришедший
    /// с сайта, не должен увидеть в браузере голый JSON.
    /// </summary>
    private static IResult Answer(bool fromForm, int statusCode, string error, string message, WebOptions web)
    {
        if (!fromForm)
            return Results.Json(new { error, message }, statusCode: statusCode);

        var back = string.IsNullOrWhiteSpace(web.SiteUrl)
            ? string.Empty
            : $"""<p><a href="{System.Net.WebUtility.HtmlEncode(web.SiteUrl)}">Вернуться на страницу покупки</a></p>""";

        var html = $"""
            <!DOCTYPE html>
            <html lang="ru">
              <head><meta charset="utf-8"><title>Не получилось</title></head>
              <body style="font-family:Segoe UI,Arial,sans-serif;padding:40px;max-width:520px;margin:0 auto;">
                <h1 style="font-size:20px;">Не получилось перейти к оплате</h1>
                <p>{System.Net.WebUtility.HtmlEncode(message)}</p>
                {back}
              </body>
            </html>
            """;

        return Results.Content(html, "text/html; charset=utf-8", null, statusCode);
    }
}
