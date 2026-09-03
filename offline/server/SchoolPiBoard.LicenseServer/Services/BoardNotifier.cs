using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.LicenseServer.Configuration;
using SchoolPiBoard.LicenseServer.Data;

namespace SchoolPiBoard.LicenseServer.Services;

/// <summary>
/// Сообщает доске об оплаченной подписке.
///
/// Доска — единственная, кто знает про тарифы и сроки; сервис ключей
/// только берёт деньги и говорит «счёт N оплачен». Продлевает срок доска
/// сама, и она же отвечает за то, чтобы повторное сообщение о том же
/// счёте не продлило его дважды.
///
/// Неудачное уведомление не теряется: счёт остаётся с пустым notified_at,
/// и фоновая служба повторит попытку. Деньги уже взяты — оставить человека
/// без подписки из-за одной сетевой ошибки нельзя.
/// </summary>
public sealed class BoardNotifier
{
    private readonly LicenseDbContext _db;
    private readonly BoardOptions _options;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<BoardNotifier> _log;

    public BoardNotifier(
        LicenseDbContext db, BoardOptions options, IHttpClientFactory http, ILogger<BoardNotifier> log)
    {
        _db = db;
        _options = options;
        _http = http;
        _log = log;
    }

    /// <summary>Отправляет уведомление и помечает счёт доставленным.</summary>
    public async Task<bool> NotifyAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            _log.LogError("Связь с доской не настроена: об оплате {InvoiceId} сообщить некому.", payment.InvoiceId);
            return false;
        }

        var body = JsonSerializer.Serialize(new
        {
            invoiceId = payment.InvoiceId.ToString(),
            userId = payment.BoardUserId,
            planCode = payment.PlanCode,
            days = payment.PeriodDays,
            amount = payment.Amount,
            autoRenew = payment.AutoRenew,
            paidAt = payment.PaidAt
        });

        var timestamp = BoardSignature.Now();

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.CallbackUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
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
                    "Доска не приняла уведомление об оплате {InvoiceId}: {Status}.",
                    payment.InvoiceId, (int)response.StatusCode);
                return false;
            }
        }
        catch (Exception error)
        {
            _log.LogError(error, "Не удалось сообщить доске об оплате {InvoiceId}.", payment.InvoiceId);
            return false;
        }

        payment.NotifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _log.LogInformation("Доска уведомлена об оплате {InvoiceId}.", payment.InvoiceId);
        return true;
    }

    /// <summary>Оплаченные подписки, о которых доска ещё не знает.</summary>
    public Task<List<Payment>> PendingAsync(CancellationToken cancellationToken)
        => _db.Payments
            .Where(x => x.Kind == Payment.KindSubscription
                        && x.Status == Payment.StatusPaid
                        && x.NotifiedAt == null)
            .OrderBy(x => x.PaidAt)
            .Take(50)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// Повторяет уведомления, которые не дошли с первого раза.
///
/// Доска могла быть недоступна ровно в ту минуту, когда пришли деньги.
/// Без повтора это означало бы оплаченную, но не выданную подписку — и
/// разбираться с ней пришлось бы руками.
/// </summary>
public sealed class BoardNotifyRetryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly BoardOptions _options;
    private readonly ILogger<BoardNotifyRetryService> _log;

    public BoardNotifyRetryService(
        IServiceScopeFactory scopes, BoardOptions options, ILogger<BoardNotifyRetryService> log)
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
                var notifier = scope.ServiceProvider.GetRequiredService<BoardNotifier>();

                foreach (var payment in await notifier.PendingAsync(stoppingToken))
                    await notifier.NotifyAsync(payment, stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Одна неудача не должна останавливать повторы навсегда.
                _log.LogError(error, "Повтор уведомлений доске не выполнен.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
