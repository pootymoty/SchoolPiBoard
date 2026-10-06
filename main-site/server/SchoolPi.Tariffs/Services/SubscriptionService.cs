using Microsoft.EntityFrameworkCore;
using SchoolPi.Tariffs.Configuration;
using SchoolPi.Tariffs.Data;

namespace SchoolPi.Tariffs.Services;

public enum BillingOutcome
{
    Ok,
    BadRequest,
    NotConfigured,
    NotFound,
}

public enum GrantOutcome
{
    /// <summary>Тариф выдан.</summary>
    Granted,
    /// <summary>Такого тарифа нет.</summary>
    UnknownPlan,
    /// <summary>У человека уже есть действующий или оплаченный вперёд тариф того же уровня или выше.</summary>
    AlreadyHasPlan,
    /// <summary>Этот тариф ему уже выдавали бесплатно — второй раз не выдаём.</summary>
    AlreadyGranted,
}

public sealed record BillingResult(BillingOutcome Outcome, Subscription? Subscription = null,
    string? PaymentUrl = null, string? Message = null)
{
    public bool IsOk => Outcome == BillingOutcome.Ok;
}

/// <summary>
/// Тарифы: покупка, продление и автопродление.
///
/// Правила по требованию владельца платформы:
/// * подписка может быть куплена поверх уже действующей — заранее, на
///   любой из тарифов (не только "выше" текущего);
/// * но только одна такая отложенная покупка сразу — вторую, пока первая
///   не началась, купить нельзя;
/// * автопродление — свойство учётной записи, а не отдельной покупки:
///   включить его можно только при оплате, а не задним числом на уже
///   действующей подписке; если новая покупка тоже с автопродлением,
///   оно переезжает на неё, снимаясь с прежней.
///
/// Бесплатные пробные попытки считает сам основной сайт по своей таблице
/// расхода ИИ. Здесь — только выданные сайтом сроки тарифа без оплаты
/// (GrantAsync): например, неделя «Базового» новому преподавателю.
/// </summary>
public sealed class SubscriptionService
{
    private readonly AppDbContext _db;
    private readonly LicenseServerClient _licenseServer;
    private readonly ILogger<SubscriptionService> _log;

    public SubscriptionService(AppDbContext db, LicenseServerClient licenseServer, ILogger<SubscriptionService> log)
    {
        _db = db;
        _licenseServer = licenseServer;
        _log = log;
    }

    /// <summary>
    /// Тариф, который действует для человека сейчас (то, что видит сайт в
    /// /status), или null — тогда действует бесплатный лимит сайта.
    ///
    /// Обычно это и есть CurrentAsync. Разница — когда сроки наложились:
    /// поверх оплаченного «Базового» выдан по акции «Стандарт» на неделю.
    /// Тогда действует тариф выше (в той же линейке), а оплаченный срок
    /// идёт своим чередом и остаётся после окончания акции.
    /// </summary>
    public async Task<Subscription?> EffectiveAsync(int externalUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var running = await _db.Subscriptions
            .Where(x => x.ExternalUserId == externalUserId && x.StartsAt <= now && x.EndsAt > now)
            .ToListAsync(cancellationToken);
        if (running.Count == 0) return null;

        var line = TariffPlans.Line(running.OrderByDescending(x => x.EndsAt).First().Plan);
        return running
            .Where(x => TariffPlans.Line(x.Plan) == line)
            .OrderByDescending(x => TariffPlans.Rank(x.Plan))
            .ThenByDescending(x => x.EndsAt)
            .First();
    }

    /// <summary>
    /// Срок, который идёт сейчас и кончается позже всех, — от него
    /// отсчитываются переносы (StartUpcomingNowAsync). Какой тариф при
    /// этом действует — EffectiveAsync.
    /// </summary>
    public Task<Subscription?> CurrentAsync(int externalUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _db.Subscriptions
            .Where(x => x.ExternalUserId == externalUserId && x.StartsAt <= now && x.EndsAt > now)
            .OrderByDescending(x => x.EndsAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Оплаченные сроки, которые ещё не начались — не больше одного по правилу выше.</summary>
    public Task<List<Subscription>> UpcomingAsync(int externalUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _db.Subscriptions
            .Where(x => x.ExternalUserId == externalUserId && x.StartsAt > now)
            .OrderBy(x => x.StartsAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<BillingResult> CreateCheckoutAsync(
        int externalUserId, string email, string planKey, bool autoRenew, string? consentText,
        CancellationToken cancellationToken)
    {
        var plan = TariffPlans.Find(planKey);
        if (plan is null)
            return new BillingResult(BillingOutcome.BadRequest, Message: "Такого тарифа нет.");

        if (!_licenseServer.IsConfigured)
        {
            return new BillingResult(BillingOutcome.NotConfigured,
                Message: "Оплата временно недоступна. Напишите нам, и мы поможем.");
        }

        var upcoming = await UpcomingAsync(externalUserId, cancellationToken);
        if (upcoming.Count > 0)
        {
            return new BillingResult(BillingOutcome.BadRequest, Message:
                "Уже куплен один тариф про запас — он начнёт действовать, когда закончится текущий. " +
                "Купить ещё один поверх него нельзя, пока он не начался.");
        }

        var invoice = await _licenseServer.CreateInvoiceAsync(
            externalUserId, email, plan.Key, plan.Title, TariffPlans.PeriodDays, plan.Price, autoRenew,
            cancellationToken);

        if (invoice is null)
            return new BillingResult(BillingOutcome.NotConfigured, Message: "Оплата временно недоступна.");

        var payment = new Payment
        {
            ExternalUserId = externalUserId,
            InvoiceId = long.Parse(invoice.InvoiceId),
            Plan = plan.Key,
            Amount = plan.Price,
            AutoRenew = autoRenew,
            ConsentText = autoRenew ? consentText : null,
            Status = Payment.StatusPending,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        return new BillingResult(BillingOutcome.Ok, PaymentUrl: invoice.PaymentUrl);
    }

    /// <summary>
    /// Оплата подтверждена сервером ключей (через /callback). Продлевает
    /// от конца уже оплаченного, если что-то ещё действует, иначе — от
    /// текущего момента. Повторное сообщение о том же счёте новую
    /// подписку не заводит.
    /// </summary>
    public async Task<BillingResult> ApplyPaymentAsync(long invoiceId, CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);
        if (payment is null)
            return new BillingResult(BillingOutcome.NotFound, Message: "Счёт не найден.");

        var already = await _db.Subscriptions
            .FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);
        if (already is not null)
            return new BillingResult(BillingOutcome.Ok, already);

        var now = DateTime.UtcNow;

        var latestEnd = await _db.Subscriptions
            .Where(x => x.ExternalUserId == payment.ExternalUserId && x.EndsAt > now)
            .OrderByDescending(x => x.EndsAt)
            .Select(x => (DateTime?)x.EndsAt)
            .FirstOrDefaultAsync(cancellationToken);

        var startsAt = latestEnd ?? now;

        var subscription = new Subscription
        {
            ExternalUserId = payment.ExternalUserId,
            Plan = payment.Plan,
            StartsAt = startsAt,
            EndsAt = startsAt.AddDays(TariffPlans.PeriodDays),
            InvoiceId = invoiceId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Subscriptions.Add(subscription);

        payment.Status = Payment.StatusPaid;
        payment.PaidAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        await ApplyAutoRenewAsync(payment.ExternalUserId, subscription.Id, payment.AutoRenew, cancellationToken);

        _log.LogInformation("Счёт {InvoiceId} принят: пользователь {UserId}, тариф {Plan}.",
            invoiceId, payment.ExternalUserId, payment.Plan);

        return new BillingResult(BillingOutcome.Ok, subscription);
    }

    /// <summary>
    /// Переносит автопродление на указанную подписку, снимая со всех
    /// прочих действующих или отложенных — оно ровно одно на аккаунт.
    /// </summary>
    public async Task ApplyAutoRenewAsync(
        int externalUserId, Guid subscriptionId, bool autoRenew, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var mine = await _db.Subscriptions
            .Where(x => x.ExternalUserId == externalUserId && x.EndsAt > now)
            .ToListAsync(cancellationToken);

        foreach (var subscription in mine)
        {
            var wanted = subscription.Id == subscriptionId && autoRenew;
            if (subscription.AutoRenew == wanted) continue;

            subscription.AutoRenew = wanted;
            subscription.UpdatedAt = now;
            if (!wanted) subscription.RenewalNoticeAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Отключает автопродление у последнего оплаченного срока — то, чем
    /// заканчивается кнопка «Отключить автопродление» в личном кабинете.
    /// Включить обратно можно только новой покупкой.
    /// </summary>
    public async Task<bool> CancelAutoRenewAsync(int externalUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var last = await _db.Subscriptions
            .Where(x => x.ExternalUserId == externalUserId && x.EndsAt > now && x.AutoRenew)
            .OrderByDescending(x => x.EndsAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (last is null) return false;

        last.AutoRenew = false;
        last.RenewalNoticeAt = null;
        last.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Переносит отложенную покупку на «сейчас» — остаток текущего срока
    /// сгорает, зато человек сразу получает тариф, за который уже
    /// заплатил, не дожидаясь даты начала. Работает в любую сторону (не
    /// только «выше» текущего): у тарифов ИИ-функций нет единой шкалы
    /// уровней, как у тарифов доски, и решение — за человеком, он видит
    /// предупреждение о сгорающем остатке перед подтверждением на сайте.
    /// </summary>
    public async Task<bool> StartUpcomingNowAsync(int externalUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var running = await CurrentAsync(externalUserId, cancellationToken);
        if (running is null) return false;

        var next = await _db.Subscriptions
            .Where(x => x.ExternalUserId == externalUserId && x.StartsAt > now)
            .OrderBy(x => x.StartsAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is null) return false;

        var shift = next.StartsAt - now;

        running.EndsAt = now;
        running.AutoRenew = false;
        running.UpdatedAt = now;

        next.StartsAt -= shift;
        next.EndsAt -= shift;
        next.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        _log.LogInformation("Подписка {UserId}: отложенный тариф {Plan} запущен досрочно.",
            externalUserId, next.Plan);

        return true;
    }

    /// <summary>
    /// Выдаёт тариф без оплаты на days дней с текущего момента — пробный
    /// период, который назначает основной сайт (новым преподавателям и
    /// разово — уже работающим).
    ///
    /// Выданный срок — обычная подписка, только без счёта (InvoiceId =
    /// null): по этому признаку его и отличают от купленного. Отдельной
    /// колонки не нужно, схема базы не меняется. Автопродление у него
    /// выключено, а DueForRenewalAsync и так берёт только подписки со
    /// счётом — списать за выданный срок нечего и не с чего.
    ///
    /// Не выдаёт, если у человека уже есть действующий или оплаченный
    /// вперёд тариф той же линейки того же уровня или выше, и если этот
    /// тариф ему уже выдавали бесплатно раньше — повторный вызов ничего не
    /// меняет (once = true, так выдаётся пробная неделя). Для акций —
    /// once = false: тот же тариф можно выдать снова (к Дню учителя, потом
    /// к Новому году), но не тому, у кого он или выше уже действует.
    /// Покупка во время выданного срока работает как обычно:
    /// оплаченный период начнётся после его окончания (ApplyPaymentAsync).
    /// </summary>
    public async Task<(GrantOutcome Outcome, Subscription? Subscription)> GrantAsync(
        int externalUserId, string planKey, int days, bool once, CancellationToken cancellationToken)
    {
        var plan = TariffPlans.Find(planKey);
        if (plan is null || days < 1 || days > 366)
            return (GrantOutcome.UnknownPlan, null);

        var alreadyGranted = once && await _db.Subscriptions.AnyAsync(
            x => x.ExternalUserId == externalUserId && x.InvoiceId == null && x.Plan == plan.Key,
            cancellationToken);
        if (alreadyGranted)
            return (GrantOutcome.AlreadyGranted, null);

        var now = DateTime.UtcNow;
        var held = await _db.Subscriptions
            .Where(x => x.ExternalUserId == externalUserId && x.EndsAt > now)
            .Select(x => x.Plan)
            .ToListAsync(cancellationToken);

        var line = TariffPlans.Line(plan.Key);
        var rank = TariffPlans.Rank(plan.Key);
        if (held.Any(key => TariffPlans.Line(key) == line && TariffPlans.Rank(key) >= rank))
            return (GrantOutcome.AlreadyHasPlan, null);

        var subscription = new Subscription
        {
            ExternalUserId = externalUserId,
            Plan = plan.Key,
            StartsAt = now,
            EndsAt = now.AddDays(days),
            AutoRenew = false,
            InvoiceId = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(cancellationToken);

        _log.LogInformation("Пользователю {UserId} выдан тариф {Plan} на {Days} дн. без оплаты.",
            externalUserId, plan.Key, days);

        return (GrantOutcome.Granted, subscription);
    }

    /// <summary>Подписки, которые пора продлевать: с автопродлением, кончающиеся в ближайшие сутки.</summary>
    public Task<List<Subscription>> DueForRenewalAsync(TimeSpan ahead, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var edge = now + ahead;

        return _db.Subscriptions
            .Where(x => x.AutoRenew && x.EndsAt > now && x.EndsAt <= edge && x.InvoiceId != null)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Записывает выставленный сервером ключей счёт за автопродление —
    /// тот же путь, что и у обычной покупки, только без участия человека.
    /// </summary>
    public async Task RecordPendingRecurringPaymentAsync(
        int externalUserId, long invoiceId, string planKey, decimal amount, CancellationToken cancellationToken)
    {
        _db.Payments.Add(new Payment
        {
            ExternalUserId = externalUserId,
            InvoiceId = invoiceId,
            Plan = planKey,
            Amount = amount,
            AutoRenew = true,
            Status = Payment.StatusPending,
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
