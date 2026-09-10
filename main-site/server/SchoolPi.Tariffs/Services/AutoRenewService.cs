using SchoolPi.Tariffs.Configuration;

namespace SchoolPi.Tariffs.Services;

/// <summary>
/// Автопродление подписок — копия AutoRenewService из
/// schoolpiboard_online/server, адаптированная под один срок (30 дней) и
/// без своей почты.
///
/// Когда продлевать — решает этот сервис: сроки знает он. Списывает
/// сервер ключей — карта и пароли Робокассы живут только там. Об успехе
/// сервис узнаёт обычным сообщением об оплате на /callback, как после
/// ручной покупки, — отдельного пути «продлить сразу» нет намеренно,
/// иначе продление засчитывалось бы до того, как деньги действительно
/// пришли.
///
/// TODO: предупреждение письмом за несколько дней до списания — то, что
/// у доски делает DueForNoticeAsync/EmailTemplates.RenewalSoon — здесь
/// пока не реализовано: у сервиса ещё нет своей отправки почты. Колонка
/// renewal_notice_at в схеме уже заведена под это, чтобы не переезжать
/// на новую версию базы, когда почта появится.
/// </summary>
public sealed class AutoRenewService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>За сколько до конца просим списать.</summary>
    private static readonly TimeSpan Ahead = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly TariffOptions _options;
    private readonly ILogger<AutoRenewService> _log;

    public AutoRenewService(IServiceScopeFactory scopes, TariffOptions options, ILogger<AutoRenewService> log)
    {
        _scopes = scopes;
        _options = options;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.LicenseServer.IsConfigured) return;

        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                _log.LogError(error, "Автопродление не выполнено.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();

        var subscriptions = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
        var licenseServer = scope.ServiceProvider.GetRequiredService<LicenseServerClient>();

        foreach (var due in await subscriptions.DueForRenewalAsync(Ahead, cancellationToken))
        {
            // Человек уже оплатил вперёд — например, купил тот же или
            // другой тариф, который встанет следующим. Списывать за
            // продление сверх этого нельзя: он заплатил бы дважды за одно
            // и то же время.
            var queued = await subscriptions.UpcomingAsync(due.ExternalUserId, cancellationToken);
            if (queued.Count > 0)
            {
                _log.LogInformation(
                    "Подписка {Id}: дальше уже оплачен другой срок, автопродление пропущено.", due.Id);
                continue;
            }

            var plan = TariffPlans.Find(due.Plan);
            if (plan is null)
            {
                _log.LogWarning("Подписка {Id}: тариф {Plan} больше не продаётся, автопродление пропущено.",
                    due.Id, due.Plan);
                continue;
            }

            var charged = await licenseServer.ChargeRecurringAsync(
                due.ExternalUserId, plan.Key, plan.Title, TariffPlans.PeriodDays, plan.Price,
                due.InvoiceId!.Value, cancellationToken);

            if (charged is null)
            {
                // Списание не прошло — человек просто опустится на
                // бесплатный лимит сайта, ничего не потеряв.
                _log.LogWarning("Автопродление подписки {Id} не прошло.", due.Id);
                continue;
            }

            await subscriptions.RecordPendingRecurringPaymentAsync(
                due.ExternalUserId, long.Parse(charged.InvoiceId), plan.Key, plan.Price, cancellationToken);

            // Больше по этой подписке не списываем: продление придёт новой
            // строкой через /callback, и автопродление переедет на неё.
            await subscriptions.ApplyAutoRenewAsync(due.ExternalUserId, due.Id, false, cancellationToken);

            _log.LogInformation("Автопродление подписки {Id}: выставлен счёт {Invoice}.",
                due.Id, charged.InvoiceId);
        }
    }
}
