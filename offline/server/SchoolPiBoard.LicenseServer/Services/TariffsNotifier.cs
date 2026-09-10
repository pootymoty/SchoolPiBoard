using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.LicenseServer.Configuration;
using SchoolPiBoard.LicenseServer.Data;

namespace SchoolPiBoard.LicenseServer.Services;

/// <summary>
/// Сообщает сервису тарифов основного сайта об оплаченном тарифе.
///
/// Копия <see cref="BoardNotifier"/> для третьего продукта: сервис ключей
/// только берёт деньги и говорит «счёт N оплачен», продлевает срок сам
/// сервис тарифов.
/// </summary>
public sealed class TariffsNotifier
{
    private readonly LicenseDbContext _db;
    private readonly TariffsOptions _options;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<TariffsNotifier> _log;

    public TariffsNotifier(
        LicenseDbContext db, TariffsOptions options, IHttpClientFactory http, ILogger<TariffsNotifier> log)
    {
        _db = db;
        _options = options;
        _http = http;
        _log = log;
    }

    public async Task<bool> NotifyAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            _log.LogError("Связь с сервисом тарифов не настроена: об оплате {InvoiceId} сообщить некому.",
                payment.InvoiceId);
            return false;
        }

        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            invoiceId = payment.InvoiceId.ToString(),
            userId = payment.TariffUserId,
            planCode = payment.PlanCode,
            days = payment.PeriodDays,
            amount = payment.Amount,
            autoRenew = payment.AutoRenew,
            paidAt = payment.PaidAt
        });

        var timestamp = BoardSignature.Now();

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.CallbackUrl)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

        request.Headers.Add(BoardSignature.TimestampHeader, timestamp);
        request.Headers.Add(BoardSignature.SignatureHeader,
            BoardSignature.Sign(_options.SharedSecret, timestamp, body));

        try
        {
            using var response = await _http.CreateClient().SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogError(
                    "Сервис тарифов не принял уведомление об оплате {InvoiceId}: {Status}.",
                    payment.InvoiceId, (int)response.StatusCode);
                return false;
            }
        }
        catch (Exception error)
        {
            _log.LogError(error, "Не удалось сообщить сервису тарифов об оплате {InvoiceId}.", payment.InvoiceId);
            return false;
        }

        payment.NotifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _log.LogInformation("Сервис тарифов уведомлён об оплате {InvoiceId}.", payment.InvoiceId);
        return true;
    }

    public Task<List<Payment>> PendingAsync(CancellationToken cancellationToken)
        => _db.Payments
            .Where(x => x.Kind == Payment.KindTariff
                        && x.Status == Payment.StatusPaid
                        && x.NotifiedAt == null)
            .OrderBy(x => x.PaidAt)
            .Take(50)
            .ToListAsync(cancellationToken);
}

/// <summary>Повторяет уведомления сервису тарифов, которые не дошли с первого раза.</summary>
public sealed class TariffsNotifyRetryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly TariffsOptions _options;
    private readonly ILogger<TariffsNotifyRetryService> _log;

    public TariffsNotifyRetryService(
        IServiceScopeFactory scopes, TariffsOptions options, ILogger<TariffsNotifyRetryService> log)
    {
        _scopes = scopes;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured) return;

        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<TariffsNotifier>();

                foreach (var payment in await notifier.PendingAsync(stoppingToken))
                    await notifier.NotifyAsync(payment, stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _log.LogError(error, "Повтор уведомлений сервису тарифов не выполнен.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
