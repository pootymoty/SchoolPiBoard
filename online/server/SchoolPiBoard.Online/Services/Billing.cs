using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.Online.Configuration;
using SchoolPiBoard.Online.Data;

namespace SchoolPiBoard.Online.Services;

/// <summary>
/// Ссылка на оплату и проверка ответа платёжной системы.
///
/// Робокасса выбрана потому, что доступна самозанятому продавцу в России:
/// Stripe для этого статуса не подходит. Карту и деньги сервис не видит —
/// только подписанные сообщения.
/// </summary>
public sealed class RobokassaService
{
    private readonly PaymentOptions _options;

    public RobokassaService(PaymentOptions options)
    {
        _options = options;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string BuildPaymentUrl(long invoiceId, decimal amount, string description, string email)
    {
        var sum = FormatSum(amount);

        var parameters = new List<string>
        {
            "MerchantLogin=" + Uri.EscapeDataString(_options.MerchantLogin),
            "OutSum=" + Uri.EscapeDataString(sum),
            "InvId=" + invoiceId.ToString(CultureInfo.InvariantCulture),
            "Description=" + Uri.EscapeDataString(description),
            "Culture=ru",
            "Encoding=utf-8"
        };

        // Состав чека Робокасса требует уже закодированным, и в подпись
        // попадает ровно та же строка — на этом чаще всего ломается интеграция.
        string? encodedReceipt = null;
        if (_options.SendReceipt)
        {
            encodedReceipt = Uri.EscapeDataString(BuildReceiptJson(amount, description));
            parameters.Add("Receipt=" + encodedReceipt);
        }

        if (!string.IsNullOrWhiteSpace(email))
            parameters.Add("Email=" + Uri.EscapeDataString(email));

        if (_options.IsTest)
            parameters.Add("IsTest=1");

        var signatureSource = encodedReceipt is null
            ? $"{_options.MerchantLogin}:{sum}:{invoiceId}:{_options.Password1}"
            : $"{_options.MerchantLogin}:{sum}:{invoiceId}:{encodedReceipt}:{_options.Password1}";

        parameters.Add("SignatureValue=" + Md5(signatureSource));

        return _options.PaymentUrl + "?" + string.Join("&", parameters);
    }

    public bool VerifyResultSignature(string? outSum, string? invoiceId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(outSum) || string.IsNullOrWhiteSpace(invoiceId) || string.IsNullOrWhiteSpace(signature))
            return false;

        var expected = Md5($"{outSum}:{invoiceId}:{_options.Password2}");

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    public static string FormatSum(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private string BuildReceiptJson(decimal amount, string description) => JsonSerializer.Serialize(new
    {
        sno = _options.TaxSystem,
        items = new[]
        {
            new
            {
                name = description,
                quantity = 1,
                sum = amount,
                payment_method = "full_payment",
                payment_object = "service",
                tax = _options.Tax
            }
        }
    });

    private static string Md5(string value)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public enum BillingOutcome
{
    Ok,
    BadRequest,
    /// <summary>Пробный период уже был.</summary>
    TrialUsed,
    NotConfigured,
    NotFound
}

public sealed record BillingResult(BillingOutcome Outcome, Subscription? Subscription = null, string? PaymentUrl = null, string? Message = null)
{
    public bool IsOk => Outcome == BillingOutcome.Ok;
}

/// <summary>Подписка: пробный период, оплата, продление, автопродление, отмена.</summary>
public sealed class SubscriptionService
{
    private readonly AppDbContext _db;
    private readonly RobokassaService _robokassa;
    private readonly IEmailSender _email;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        AppDbContext db,
        RobokassaService robokassa,
        IEmailSender email,
        ILogger<SubscriptionService> logger)
    {
        _db = db;
        _robokassa = robokassa;
        _email = email;
        _logger = logger;
    }

    public Task<Subscription?> GetAsync(Guid userId, CancellationToken cancellationToken)
        => _db.Subscriptions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    /// <summary>Есть ли право создавать доски прямо сейчас.</summary>
    public async Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subscription = await GetAsync(userId, cancellationToken);
        return subscription is not null && subscription.IsActive(DateTime.UtcNow);
    }

    public async Task<BillingResult> StartTrialAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return new BillingResult(BillingOutcome.NotFound, Message: "Учётная запись не найдена.");

        if (user.TrialUsedAt is not null)
            return new BillingResult(BillingOutcome.TrialUsed, Message: "Пробный период уже использован.");

        var now = DateTime.UtcNow;
        var existing = await GetAsync(userId, cancellationToken);

        if (existing is not null && existing.IsActive(now))
            return new BillingResult(BillingOutcome.BadRequest, existing, Message: "Подписка уже действует.");

        var subscription = existing ?? new Subscription { UserId = userId, CreatedAt = now };

        subscription.Kind = Subscription.KindTrial;
        subscription.PlanDays = SubscriptionPlans.TrialDays;
        subscription.Status = Subscription.StatusActive;
        subscription.StartedAt = now;
        subscription.ExpiresAt = now.AddDays(SubscriptionPlans.TrialDays);
        subscription.AutoRenew = false;
        subscription.UpdatedAt = now;

        if (existing is null)
            _db.Subscriptions.Add(subscription);

        user.TrialUsedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return new BillingResult(BillingOutcome.Ok, subscription);
    }

    public async Task<BillingResult> CreateCheckoutAsync(Guid userId, int planDays, CancellationToken cancellationToken)
    {
        var plan = SubscriptionPlans.Find(planDays);
        if (plan is null)
            return new BillingResult(BillingOutcome.BadRequest, Message: "Такого тарифа нет.");

        if (!_robokassa.IsConfigured)
        {
            return new BillingResult(BillingOutcome.NotConfigured,
                Message: "Оплата временно недоступна. Напишите нам, и мы поможем.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
            return new BillingResult(BillingOutcome.NotFound, Message: "Учётная запись не найдена.");

        var invoiceId = await _db.Database
            .SqlQueryRaw<long>("SELECT nextval('payments_invoice_id_seq') AS \"Value\"")
            .FirstAsync(cancellationToken);

        var payment = new Payment
        {
            UserId = userId,
            InvoiceId = invoiceId,
            PlanDays = plan.Days,
            Amount = plan.Price,
            Provider = "robokassa",
            Status = Payment.StatusPending,
            CreatedAt = DateTime.UtcNow
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        var description = $"Подписка SchoolPiBoard, {plan.Title}";
        var url = _robokassa.BuildPaymentUrl(invoiceId, plan.Price, description, user.Email);

        return new BillingResult(BillingOutcome.Ok, PaymentUrl: url);
    }

    /// <summary>
    /// Оплата подтверждена платёжной системой. Продление добавляет дни
    /// к остатку, а не обнуляет его: человек заплатил за срок, а не за дату.
    /// </summary>
    public async Task<BillingResult> ApplyPaymentAsync(long invoiceId, CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);
        if (payment is null)
            return new BillingResult(BillingOutcome.NotFound, Message: "Счёт не найден.");

        if (payment.Status == Payment.StatusPaid)
        {
            // Повторное уведомление о том же счёте: срок уже продлён.
            var current = await GetAsync(payment.UserId, cancellationToken);
            return new BillingResult(BillingOutcome.Ok, current);
        }

        var now = DateTime.UtcNow;
        var subscription = await GetAsync(payment.UserId, cancellationToken);

        if (subscription is null)
        {
            subscription = new Subscription
            {
                UserId = payment.UserId,
                CreatedAt = now,
                StartedAt = now,
                ExpiresAt = now.AddDays(payment.PlanDays)
            };
            _db.Subscriptions.Add(subscription);
        }
        else
        {
            var from = subscription.IsActive(now) ? subscription.ExpiresAt : now;
            subscription.ExpiresAt = from.AddDays(payment.PlanDays);
        }

        subscription.Kind = Subscription.KindPaid;
        subscription.PlanDays = payment.PlanDays;
        subscription.Status = Subscription.StatusActive;
        subscription.Provider = payment.Provider;
        subscription.UpdatedAt = now;

        payment.Status = Payment.StatusPaid;
        payment.PaidAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == payment.UserId, cancellationToken);
        if (user is not null)
        {
            var letter = EmailTemplates.SubscriptionActivated(
                subscription.ExpiresAt.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU")));

            await _email.SendAsync(user.Email, letter.Subject, letter.Html, letter.Text, cancellationToken);
        }

        _logger.LogInformation("Счёт {InvoiceId} оплачен, подписка продлена до {ExpiresAt:u}.", invoiceId, subscription.ExpiresAt);
        return new BillingResult(BillingOutcome.Ok, subscription);
    }

    public async Task<BillingResult> SetAutoRenewAsync(Guid userId, bool enabled, CancellationToken cancellationToken)
    {
        var subscription = await GetAsync(userId, cancellationToken);
        if (subscription is null)
            return new BillingResult(BillingOutcome.NotFound, Message: "Подписки нет.");

        subscription.AutoRenew = enabled;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new BillingResult(BillingOutcome.Ok, subscription);
    }

    /// <summary>
    /// Отмена подписки. Доступ сохраняется до конца оплаченного срока —
    /// деньги за него уже взяты, отбирать его было бы нечестно.
    /// </summary>
    public async Task<BillingResult> CancelAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subscription = await GetAsync(userId, cancellationToken);
        if (subscription is null)
            return new BillingResult(BillingOutcome.NotFound, Message: "Подписки нет.");

        subscription.Status = Subscription.StatusCanceled;
        subscription.AutoRenew = false;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new BillingResult(BillingOutcome.Ok, subscription);
    }
}
