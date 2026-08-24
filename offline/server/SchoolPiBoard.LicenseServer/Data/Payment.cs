namespace SchoolPiBoard.LicenseServer.Data;

/// <summary>Счёт на оплату лицензии. Создаётся до перехода на форму оплаты.</summary>
public class Payment
{
    public const string StatusPending = "pending";
    public const string StatusPaid = "paid";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Номер счёта для платёжной системы (InvId у Робокассы).</summary>
    public long InvoiceId { get; set; }

    public string Email { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>Платёжная система: сейчас всегда «robokassa».</summary>
    public string Provider { get; set; } = string.Empty;

    public string Status { get; set; } = StatusPending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PaidAt { get; set; }

    /// <summary>Лицензия, выпущенная по этой оплате.</summary>
    public Guid? LicenseId { get; set; }
}
