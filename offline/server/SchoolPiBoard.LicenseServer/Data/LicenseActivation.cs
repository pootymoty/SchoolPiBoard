namespace SchoolPiBoard.LicenseServer.Data;

/// <summary>Занятый слот устройства. На одну лицензию их не больше двух.</summary>
public class LicenseActivation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LicenseId { get; set; }

    public License? License { get; set; }

    /// <summary>Отпечаток компьютера, присланный клиентом.</summary>
    public string HardwareId { get; set; } = string.Empty;

    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastValidatedAt { get; set; } = DateTime.UtcNow;
}
