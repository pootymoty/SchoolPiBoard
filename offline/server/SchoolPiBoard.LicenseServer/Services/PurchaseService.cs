using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.LicenseServer.Data;

namespace SchoolPiBoard.LicenseServer.Services;

/// <summary>Счета на оплату: выставление и отметка об оплате.</summary>
public sealed class PurchaseService
{
    public const string ProviderRobokassa = "robokassa";

    private readonly LicenseDbContext _db;

    public PurchaseService(LicenseDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Заводит счёт. Номер берётся из последовательности в базе: платёжной
    /// системе нужен уникальный возрастающий номер, а не GUID.
    /// </summary>
    public async Task<Payment> CreateAsync(string email, decimal amount, string provider, CancellationToken cancellationToken)
    {
        var invoiceId = await NextInvoiceIdAsync(cancellationToken);

        var payment = new Payment
        {
            InvoiceId = invoiceId,
            Email = email,
            Amount = amount,
            Provider = provider,
            Status = Payment.StatusPending,
            CreatedAt = DateTime.UtcNow
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        return payment;
    }

    /// <summary>
    /// Счёт за подписку на онлайн-доску.
    ///
    /// Номер берётся из той же последовательности, что и у лицензий:
    /// у Робокассы номер счёта один на магазин, и вторая нумерация рано
    /// или поздно выдала бы занятый номер — оплата пришла бы на счёт,
    /// которого сервис не знает.
    /// </summary>
    public async Task<Payment> CreateForBoardAsync(
        long boardUserId,
        string email,
        string planCode,
        int days,
        decimal amount,
        string description,
        bool autoRenew,
        long? previousInvoiceId,
        CancellationToken cancellationToken)
    {
        var invoiceId = await NextInvoiceIdAsync(cancellationToken);

        var payment = new Payment
        {
            InvoiceId = invoiceId,
            Email = email,
            Amount = amount,
            Provider = ProviderRobokassa,
            Status = Payment.StatusPending,
            CreatedAt = DateTime.UtcNow,
            Kind = Payment.KindSubscription,
            BoardUserId = boardUserId,
            PlanCode = planCode,
            PeriodDays = days,
            Description = description,
            AutoRenew = autoRenew,
            PreviousInvoiceId = previousInvoiceId
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        return payment;
    }

    /// <summary>Отмечает оплату подписки. Лицензия здесь не выпускается — её нет.</summary>
    public async Task MarkPaidAsync(Payment payment, CancellationToken cancellationToken)
    {
        payment.Status = Payment.StatusPaid;
        payment.PaidAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<Payment?> FindByInvoiceAsync(long invoiceId, CancellationToken cancellationToken)
        => _db.Payments.FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);

    private Task<long> NextInvoiceIdAsync(CancellationToken cancellationToken)
        => _db.Database
            .SqlQueryRaw<long>("SELECT nextval('payments_invoice_id_seq') AS \"Value\"")
            .FirstAsync(cancellationToken);

    public async Task MarkPaidAsync(Payment payment, Guid licenseId, CancellationToken cancellationToken)
    {
        payment.Status = Payment.StatusPaid;
        payment.PaidAt = DateTime.UtcNow;
        payment.LicenseId = licenseId;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
