namespace Whiteboard.LicenseServer.Data;

/// <summary>Подписка на веб-версию.</summary>
public class Subscription
{
    public const string PlanMonthly = "monthly";
    public const string PlanYearly = "yearly";

    public const string StatusTrialing = "trialing";
    public const string StatusActive = "active";
    public const string StatusPastDue = "past_due";
    public const string StatusCanceled = "canceled";

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>«robokassa», «stripe» — платёжная система, выдавшая подписку.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Идентификатор подписки на стороне платёжной системы.</summary>
    public string? ExternalId { get; set; }

    public string Plan { get; set; } = PlanMonthly;

    public string Status { get; set; } = StatusTrialing;

    public DateTime? TrialEndsAt { get; set; }

    public DateTime? CurrentPeriodEnd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Даёт ли подписка право работать прямо сейчас.</summary>
    public bool IsUsable(DateTime now)
    {
        if (Status == StatusTrialing)
            return TrialEndsAt is null || now < TrialEndsAt.Value;

        if (Status == StatusActive)
            return CurrentPeriodEnd is null || now < CurrentPeriodEnd.Value;

        // past_due и canceled доступ не дают.
        return false;
    }
}
