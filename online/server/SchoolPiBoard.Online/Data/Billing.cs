namespace SchoolPiBoard.Online.Data;

/// <summary>Подписка. У пользователя она одна: продление сдвигает дату окончания.</summary>
public class Subscription
{
    public const string KindTrial = "trial";
    public const string KindPaid = "paid";

    public const string StatusActive = "active";
    public const string StatusCanceled = "canceled";

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Kind { get; set; } = KindPaid;

    public int PlanDays { get; set; }

    public string Status { get; set; } = StatusActive;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public bool AutoRenew { get; set; }

    public string? Provider { get; set; }

    public string? ExternalId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Даёт ли подписка право работать прямо сейчас. Отменённая подписка
    /// действует до конца оплаченного срока — деньги за него уже взяты.
    /// </summary>
    public bool IsActive(DateTime now) => now < ExpiresAt;
}

public class Payment
{
    public const string StatusPending = "pending";
    public const string StatusPaid = "paid";

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Номер счёта для платёжной системы.</summary>
    public long InvoiceId { get; set; }

    public int PlanDays { get; set; }

    public decimal Amount { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Status { get; set; } = StatusPending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PaidAt { get; set; }
}
