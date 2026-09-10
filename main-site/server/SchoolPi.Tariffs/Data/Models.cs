namespace SchoolPi.Tariffs.Data;

/// <summary>
/// Оплаченный срок тарифа. У пользователя их может быть несколько:
/// действующий и один отложенный, оплаченный заранее (см.
/// SubscriptionService.UpcomingAsync). Продление — новая строка, а не
/// правка старой: так видно историю платежей самим фактом строк.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Id пользователя на основном сайте (не свой — своих пользователей здесь нет).</summary>
    public int ExternalUserId { get; set; }

    public string Plan { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; } = DateTime.UtcNow;

    public DateTime EndsAt { get; set; }

    /// <summary>
    /// Автопродление. Свойство учётной записи, а не отдельной покупки:
    /// списание должно быть одно, и решает его последняя оплата — при
    /// новой покупке с автопродлением снимается с прежней (см.
    /// SubscriptionService.ApplyAutoRenewAsync).
    /// </summary>
    public bool AutoRenew { get; set; }

    /// <summary>Счёт, по которому эта подписка была оплачена — нужен для следующего автосписания.</summary>
    public long? InvoiceId { get; set; }

    /// <summary>Когда в последний раз предупредили о скором списании. Пусто — ещё не предупреждали.</summary>
    public DateTime? RenewalNoticeAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive(DateTime now) => StartsAt <= now && now < EndsAt;
    public bool IsUpcoming(DateTime now) => StartsAt > now;
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

    public bool AutoRenew { get; set; }

    public string Status { get; set; } = StatusPending;

    /// <summary>
    /// Текст согласия на автосписание, под которым нажал покупатель — не
    /// сама галочка, а то, что было рядом с ней написано на момент
    /// покупки. Пусто у покупок без автопродления. См. требование
    /// Робокассы: «сохраняйте историю согласий пользователей».
    /// </summary>
    public string? ConsentText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PaidAt { get; set; }
}
