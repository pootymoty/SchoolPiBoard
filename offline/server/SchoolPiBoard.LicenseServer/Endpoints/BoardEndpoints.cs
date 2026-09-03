using System.Globalization;
using System.Text.Json;
using SchoolPiBoard.LicenseServer.Configuration;
using SchoolPiBoard.LicenseServer.Data;
using SchoolPiBoard.LicenseServer.Services;

namespace SchoolPiBoard.LicenseServer.Endpoints;

/// <summary>Заказ на подписку: доска говорит, кому и за что выставить счёт.</summary>
public sealed record InvoiceRequest(
    long UserId,
    string? Email,
    string? PlanCode,
    string? PlanName,
    int Days,
    decimal Amount,
    bool AutoRenew);

/// <summary>Повторное списание по ранее оплаченному счёту.</summary>
public sealed record RecurringRequest(
    long UserId,
    string? PlanCode,
    string? PlanName,
    int Days,
    decimal Amount,
    long PreviousInvoiceId);

/// <summary>
/// Подписка на онлайн-доску.
///
/// Доска не знает паролей Робокассы и никогда к ней не обращается — это
/// правило владельца. Она только просит счёт и получает сообщение об
/// оплате; всё платёжное живёт здесь.
///
/// Запросы подписаны общим секретом. Без него подписки выключены целиком:
/// открытый эндпоинт, выставляющий счета от чужого имени, — худшее, что
/// можно оставить включённым «на всякий случай».
/// </summary>
public static class BoardEndpoints
{
    /// <summary>Сроки, которые доска продаёт. Всё прочее — отказ, а не догадка.</summary>
    private static readonly int[] Periods = { 30, 90, 180, 365 };

    public static void MapBoardEndpoints(this WebApplication app)
    {
        app.MapPost("/board/invoice", async (
            HttpRequest http, BoardOptions board,
            PurchaseService purchases, RobokassaService payments,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("Board");

            var (ok, body, error) = await ReadSignedAsync(http, board, ct);
            if (!ok) return error!;

            var request = Parse<InvoiceRequest>(body);
            if (request is null || !IsSane(request.UserId, request.Days, request.Amount))
                return Results.BadRequest(new { message = "Заказ не разобран." });

            if (!payments.CanSellSubscriptions)
            {
                logger.LogError("Заказ подписки при ненастроенной Робокассе.");
                return Results.Json(new { message = "Оплата временно недоступна." }, statusCode: 503);
            }

            var description = Describe(request.PlanName, request.Days);

            var payment = await purchases.CreateForBoardAsync(
                request.UserId,
                EmailAddress.Normalize(request.Email) ?? string.Empty,
                request.PlanCode ?? string.Empty,
                request.Days,
                request.Amount,
                description,
                request.AutoRenew,
                previousInvoiceId: null,
                ct);

            var url = payments.BuildPaymentUrl(
                payment.InvoiceId, payment.Email, request.Amount, description, request.AutoRenew);

            logger.LogInformation(
                "Выставлен счёт {InvoiceId} за подписку доски: пользователь {UserId}, {Days} дн.",
                payment.InvoiceId, request.UserId, request.Days);

            return Results.Ok(new
            {
                invoiceId = payment.InvoiceId.ToString(CultureInfo.InvariantCulture),
                paymentUrl = url,
                amount = RobokassaService.FormatSum(request.Amount)
            });
        });

        // Автопродление: доска сама решает, когда пора, — сроки знает она.
        // Здесь только списание: карта привязана к первому счёту, и Робокасса
        // спишет по нему без участия человека.
        app.MapPost("/board/recurring", async (
            HttpRequest http, BoardOptions board,
            PurchaseService purchases, RobokassaService payments, IHttpClientFactory clients,
            ILoggerFactory loggers, CancellationToken ct) =>
        {
            var logger = loggers.CreateLogger("Board");

            var (ok, body, error) = await ReadSignedAsync(http, board, ct);
            if (!ok) return error!;

            var request = Parse<RecurringRequest>(body);
            if (request is null || !IsSane(request.UserId, request.Days, request.Amount))
                return Results.BadRequest(new { message = "Заказ не разобран." });

            if (!payments.CanSellSubscriptions)
                return Results.Json(new { message = "Оплата временно недоступна." }, statusCode: 503);

            var first = await purchases.FindByInvoiceAsync(request.PreviousInvoiceId, ct);
            if (first is null || first.Status != Payment.StatusPaid || first.BoardUserId != request.UserId)
            {
                // Списывать по чужому или неоплаченному счёту нельзя ни при
                // каких обстоятельствах — это чужая карта.
                logger.LogWarning(
                    "Отказано в повторном списании по счёту {InvoiceId}.", request.PreviousInvoiceId);
                return Results.BadRequest(new { message = "По этому счёту повторное списание невозможно." });
            }

            var description = Describe(request.PlanName, request.Days);

            var payment = await purchases.CreateForBoardAsync(
                request.UserId, first.Email, request.PlanCode ?? string.Empty,
                request.Days, request.Amount, description,
                autoRenew: true, previousInvoiceId: request.PreviousInvoiceId, ct);

            var charged = await payments.ChargeRecurringAsync(
                clients.CreateClient(), payment.InvoiceId, request.PreviousInvoiceId,
                request.Amount, description, ct);

            if (!charged)
            {
                logger.LogWarning("Повторное списание по счёту {InvoiceId} не прошло.", payment.InvoiceId);
                return Results.Json(new { message = "Списание не прошло." }, statusCode: 402);
            }

            // Об оплате доска узнает обычным уведомлением: Робокасса пришлёт
            // его на ResultURL так же, как после ручной оплаты.
            logger.LogInformation("Повторное списание отправлено, счёт {InvoiceId}.", payment.InvoiceId);
            return Results.Ok(new { invoiceId = payment.InvoiceId.ToString(CultureInfo.InvariantCulture) });
        });
    }

    /// <summary>Название в чеке и на форме оплаты. Человек должен видеть, за что платит.</summary>
    private static string Describe(string? planName, int days)
    {
        var plan = string.IsNullOrWhiteSpace(planName) ? "SchoolPiBoard" : planName.Trim();
        return $"Подписка SchoolPiBoard, тариф «{plan}», {days} дн.";
    }

    /// <summary>Заведомо бессмысленный заказ отсекаем до Робокассы.</summary>
    private static bool IsSane(long userId, int days, decimal amount)
        => userId > 0 && Periods.Contains(days) && amount > 0 && amount < 1_000_000m;

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

    /// <summary>
    /// Читает тело и проверяет подпись. Тело читается целиком строкой:
    /// подпись считается ровно по тем байтам, что пришли, а не по тому,
    /// как их разобрал сериализатор.
    /// </summary>
    private static async Task<(bool Ok, string Body, IResult? Error)> ReadSignedAsync(
        HttpRequest http, BoardOptions board, CancellationToken cancellationToken)
    {
        if (!board.IsConfigured)
            return (false, string.Empty, Results.NotFound());

        using var reader = new StreamReader(http.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var timestamp = http.Headers[BoardSignature.TimestampHeader].ToString();
        var signature = http.Headers[BoardSignature.SignatureHeader].ToString();

        if (!BoardSignature.Verify(board.SharedSecret, timestamp, signature, body))
            return (false, string.Empty, Results.Json(new { message = "Подпись не сходится." }, statusCode: 403));

        return (true, body, null);
    }
}
