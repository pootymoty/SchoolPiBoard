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
        var invoiceId = await _db.Database
            .SqlQueryRaw<long>("SELECT nextval('payments_invoice_id_seq') AS \"Value\"")
            .FirstAsync(cancellationToken);

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

    public Task<Payment?> FindByInvoiceAsync(long invoiceId, CancellationToken cancellationToken)
        => _db.Payments.FirstOrDefaultAsync(x => x.InvoiceId == invoiceId, cancellationToken);

    public async Task MarkPaidAsync(Payment payment, Guid licenseId, CancellationToken cancellationToken)
    {
        payment.Status = Payment.StatusPaid;
        payment.PaidAt = DateTime.UtcNow;
        payment.LicenseId = licenseId;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
