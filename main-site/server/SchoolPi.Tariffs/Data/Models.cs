namespace SchoolPi.Tariffs.Data;

/// <summary>
/// Подписка на тариф. У внешнего пользователя (id с основного сайта) она
/// одна — продление сдвигает дату окончания, а не заводит новую строку.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Id пользователя на основном сайте (не свой — своих пользователей здесь нет).</summary>
    public int ExternalUserId { get; set; }

    public string Plan { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive(DateTime now) => now < ExpiresAt;
}

public class Payment
{
    public const string StatusPending = "pending";
    public const string StatusPaid = "paid";

    public Guid Id { get; set; } = Guid.NewGuid();

    public int ExternalUserId { get; set; }

    /// <summary>Номер счёта для Робокассы — из последовательности, не подбирается.</summary>
    public long InvoiceId { get; set; }

    public string Plan { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Status { get; set; } = StatusPending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PaidAt { get; set; }
}
