namespace Whiteboard.LicenseServer.Data;

/// <summary>Лицензия — результат одной оплаты. Живёт вечно, пока не отозвана.</summary>
public class License
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Ключ в виде XXXX-XXXX-XXXX-XXXX.</summary>
    public string Key { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool Revoked { get; set; }

    /// <summary>SHA-256 от Stripe payment intent id — только для распознавания повторов.</summary>
    public string? StripePaymentHash { get; set; }

    /// <summary>Когда ушло письмо с ключом. NULL — ещё не ушло.</summary>
    public DateTime? EmailSentAt { get; set; }

    public List<LicenseActivation> Activations { get; set; } = new();
}
