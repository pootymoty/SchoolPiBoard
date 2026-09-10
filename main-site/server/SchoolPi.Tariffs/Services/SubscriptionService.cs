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

public sealed record BillingResult(BillingOutcome Outcome, Subscription? Subscription = null,
    string? PaymentUrl = null, string? Message = null)
{
    public bool IsOk => Outcome == BillingOutcome.Ok;
}

/// <summary>
/// Оплата тарифа и продление подписки. Пробного периода здесь нет —
/// бесплатные попытки считает сам основной сайт по своей таблице расхода
/// ИИ, этот сервис знает только про оплаченное время.
/// </summary>
public sealed class SubscriptionService
{
    private readonly AppDbContext _db;
    private readonly RobokassaService _robokassa;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(AppDbContext db, RobokassaService robokassa, ILogger<SubscriptionService> logger)
    {
        _db = db;
        _robokassa = robokassa;
        _logger = logger;
    }

    public Task<Subscription?> GetAsync(int externalUserId, CancellationToken cancellationToken)
        => _db.Subscriptions.FirstOrDefaultAsync(x => x.ExternalUserId == externalUserId, cancellationToken);

    public async Task<BillingResult> CreateCheckoutAsync(
        int externalUserId, string planKey, string? email, CancellationToken cancellationToken)
    {
        var plan = TariffPlans.Find(planKey);
        if (plan is null)
            return new BillingResult(BillingOutcome.BadRequest, Message: "Такого тарифа нет.");

        if (!_robokassa.IsConfigured)
        {
            return new BillingResult(BillingOutcome.NotConfigured,
                Message: "Оплата временно недоступна. Напишите нам, и мы поможем.");
        }

        var invoiceId = await _db.Database
            .SqlQueryRaw<long>("SELECT nextval('payments_invoice_id_seq') AS \"Value\"")
            .FirstAsync(cancellationToken);

        var payment = new Payment
        {
            ExternalUserId = externalUserId,
            InvoiceId = invoiceId,
            Plan = plan.Key,
            Amount = plan.Price,
            Status = Payment.StatusPending,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        var description = $"Тариф «{plan.Title}», school-pi.online";
        var url = _robokassa.BuildPaymentUrl(invoiceId, plan.Price, description, email);

        return new BillingResult(BillingOutcome.Ok, PaymentUrl: url);
    }

    /// <summary>
    /// Оплата подтверждена платёжной системой. Продление добавляет месяц
    /// к остатку действующей подписки, а не обнуляет его — купленное
    /// заранее время не сгорает.
    /// </summary>
    public async Task<BillingResult> ApplyPaymentAsync(long invoiceId, CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);
        if (payment is null)
            return new BillingResult(BillingOutcome.NotFound, Message: "Счёт не найден.");

        if (payment.Status == Payment.StatusPaid)
        {
            // Повторное уведомление о том же счёте: срок уже продлён,
            // отвечаем успехом без повторной записи.
            var current = await GetAsync(payment.ExternalUserId, cancellationToken);
            return new BillingResult(BillingOutcome.Ok, current);
        }

        var now = DateTime.UtcNow;
        var subscription = await GetAsync(payment.ExternalUserId, cancellationToken);

        // started_at при любой прошедшей оплате становится «сейчас» — новый
        // платёж всегда даёт полный свежий пакет токенов немедленно, сайт
        // считает расход именно от этой даты. А вот доступ по времени не
        // теряется: expires_at считается от текущего конца действия, если
        // подписка ещё не истекла, а не от «сейчас» всегда.
        var from = subscription is not null && subscription.IsActive(now) ? subscription.ExpiresAt : now;

        if (subscription is null)
        {
            subscription = new Subscription
            {
                ExternalUserId = payment.ExternalUserId,
                CreatedAt = now,
            };
            _db.Subscriptions.Add(subscription);
        }

        subscription.Plan = payment.Plan;
        subscription.StartedAt = now;
        subscription.ExpiresAt = from.AddDays(30);
        subscription.UpdatedAt = now;

        payment.Status = Payment.StatusPaid;
        payment.PaidAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Счёт {InvoiceId} оплачен, подписка {UserId} продлена до {ExpiresAt:u}.",
            invoiceId, payment.ExternalUserId, subscription.ExpiresAt);

        return new BillingResult(BillingOutcome.Ok, subscription);
    }
}
