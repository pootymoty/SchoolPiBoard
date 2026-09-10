using System.Globalization;
using System.Text.Json;
using SchoolPiBoard.LicenseServer.Configuration;
using SchoolPiBoard.LicenseServer.Data;
using SchoolPiBoard.LicenseServer.Services;

namespace SchoolPiBoard.LicenseServer.Endpoints;

/// <summary>Заказ на тариф основного сайта: сервис тарифов говорит, кому и за что выставить счёт.</summary>
public sealed record TariffInvoiceRequest(
    long UserId,
    string? Email,
    string? PlanCode,
    string? PlanName,
    int Days,
    decimal Amount,
    bool AutoRenew);

/// <summary>Повторное списание по ранее оплаченному счёту тарифа.</summary>
public sealed record TariffRecurringRequest(
    long UserId,
    string? PlanCode,
    string? PlanName,
    int Days,
    decimal Amount,
    long PreviousInvoiceId);

/// <summary>
/// Подписка на тарифы ИИ-функций основного сайта — копия <see cref="BoardEndpoints"/>
/// для третьего продукта. Сервис тарифов паролей Робокассы не знает и
/// никогда к ней не обращается; здесь только счёт и уведомление о нём.
/// </summary>
public static class TariffsEndpoints
{
    /// <summary>Единственный срок, который продаётся, — календарный месяц.</summary>
    private const int PeriodDays = 30;

    public static void MapTariffsEndpoints(this WebApplication app)
    {
        app.MapPost("/tariffs/invoice", async (
            HttpRequest http, TariffsOptions tariffs,
            PurchaseService purchases, RobokassaService payments,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("Tariffs");

            var (ok, body, error) = await ReadSignedAsync(http, tariffs, ct);
            if (!ok) return error!;

            var request = Parse<TariffInvoiceRequest>(body);
            if (request is null || !IsSane(request.UserId, request.Days, request.Amount))
                return Results.BadRequest(new { message = "Заказ не разобран." });

            if (!payments.CanSellTariffs)
            {
                logger.LogError("Заказ тарифа при ненастроенной Робокассе.");
                return Results.Json(new { message = "Оплата временно недоступна." }, statusCode: 503);
            }

            var description = Describe(request.PlanName);

            var payment = await purchases.CreateForTariffsAsync(
                request.UserId,
                EmailAddress.Normalize(request.Email) ?? string.Empty,
                request.PlanCode ?? string.Empty,
                request.Days,
                request.Amount,
                description,
                request.AutoRenew,
                previousInvoiceId: null,
                ct);

            var url = payments.BuildTariffsPaymentUrl(
                payment.InvoiceId, payment.Email, request.Amount, description, request.AutoRenew);

            logger.LogInformation(
                "Выставлен счёт {InvoiceId} за тариф основного сайта: пользователь {UserId}.",
                payment.InvoiceId, request.UserId);

            return Results.Ok(new
            {
                invoiceId = payment.InvoiceId.ToString(CultureInfo.InvariantCulture),
                paymentUrl = url,
                amount = RobokassaService.FormatSum(request.Amount)
            });
        });

        // Автопродление: сервис тарифов сам решает, когда пора — сроки
        // знает он. Здесь только списание по счёту, к которому привязана
        // карта.
        app.MapPost("/tariffs/recurring", async (
            HttpRequest http, TariffsOptions tariffs,
            PurchaseService purchases, RobokassaService payments, IHttpClientFactory clients,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("Tariffs");

            var (ok, body, error) = await ReadSignedAsync(http, tariffs, ct);
            if (!ok) return error!;

            var request = Parse<TariffRecurringRequest>(body);
            if (request is null || !IsSane(request.UserId, request.Days, request.Amount))
                return Results.BadRequest(new { message = "Заказ не разобран." });

            if (!payments.CanSellTariffs)
                return Results.Json(new { message = "Оплата временно недоступна." }, statusCode: 503);

            var first = await purchases.FindByInvoiceAsync(request.PreviousInvoiceId, ct);
            if (first is null || first.Status != Payment.StatusPaid || first.TariffUserId != request.UserId)
            {
                logger.LogWarning(
                    "Отказано в повторном списании по счёту {InvoiceId}.", request.PreviousInvoiceId);
                return Results.BadRequest(new { message = "По этому счёту повторное списание невозможно." });
            }

            var description = Describe(request.PlanName);

            var payment = await purchases.CreateForTariffsAsync(
                request.UserId, first.Email, request.PlanCode ?? string.Empty,
                request.Days, request.Amount, description,
                autoRenew: true, previousInvoiceId: request.PreviousInvoiceId, ct);

            var charged = await payments.ChargeTariffsRecurringAsync(
                clients.CreateClient(), payment.InvoiceId, request.PreviousInvoiceId,
                request.Amount, description, ct);

            if (!charged)
            {
                logger.LogWarning("Повторное списание по счёту {InvoiceId} не прошло.", payment.InvoiceId);
                return Results.Json(new { message = "Списание не прошло." }, statusCode: 402);
            }

            logger.LogInformation("Повторное списание отправлено, счёт {InvoiceId}.", payment.InvoiceId);
            return Results.Ok(new { invoiceId = payment.InvoiceId.ToString(CultureInfo.InvariantCulture) });
        });
    }

    private static string Describe(string? planName)
    {
        var plan = string.IsNullOrWhiteSpace(planName) ? "school-pi.online" : planName.Trim();
        return $"Тариф школы «{plan}», school-pi.online, {PeriodDays} дн.";
    }

    private static bool IsSane(long userId, int days, decimal amount)
        => userId > 0 && days == PeriodDays && amount > 0 && amount < 1_000_000m;

    private static T? Parse<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<(bool Ok, string Body, IResult? Error)> ReadSignedAsync(
        HttpRequest http, TariffsOptions tariffs, CancellationToken cancellationToken)
    {
        if (!tariffs.IsConfigured)
            return (false, string.Empty, Results.NotFound());

        using var reader = new StreamReader(http.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var timestamp = http.Headers[BoardSignature.TimestampHeader].ToString();
        var signature = http.Headers[BoardSignature.SignatureHeader].ToString();

        if (!BoardSignature.Verify(tariffs.SharedSecret, timestamp, signature, body))
            return (false, string.Empty, Results.Json(new { message = "Подпись не сходится." }, statusCode: 403));

        return (true, body, null);
    }
}
