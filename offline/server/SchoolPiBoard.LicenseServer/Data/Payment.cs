namespace SchoolPiBoard.LicenseServer.Data;

/// <summary>Счёт на оплату лицензии. Создаётся до перехода на форму оплаты.</summary>
public class Payment
{
    public const string StatusPending = "pending";
    public const string StatusPaid = "paid";

    /// <summary>Покупка бессрочной лицензии на офлайн-доску — то, ради чего сервис и заводился.</summary>
    public const string KindLicense = "license";

    /// <summary>Подписка на онлайн-доску. Ключ не выпускается: срок продлевает сама доска.</summary>
    public const string KindSubscription = "subscription";

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

    /// <summary>Что покупают: <see cref="KindLicense"/> или <see cref="KindSubscription"/>.</summary>
    public string Kind { get; set; } = KindLicense;

    /// <summary>Номер учётной записи на доске. У лицензии пусто.</summary>
    public long? BoardUserId { get; set; }

    /// <summary>Код тарифа доски.</summary>
    public string? PlanCode { get; set; }

    /// <summary>Срок подписки в днях.</summary>
    public int? PeriodDays { get; set; }

    /// <summary>
    /// Назначение платежа для этого счёта. У лицензии оно одно на всех и
    /// лежит в настройках, у подписки зависит от тарифа и срока.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Когда доска подтвердила, что узнала об оплате.</summary>
    public DateTime? NotifiedAt { get; set; }

    /// <summary>Согласие на автопродление, данное при первой оплате.</summary>
    public bool AutoRenew { get; set; }

    /// <summary>Счёт, по которому Робокасса списывает повторно.</summary>
    public long? PreviousInvoiceId { get; set; }
}
