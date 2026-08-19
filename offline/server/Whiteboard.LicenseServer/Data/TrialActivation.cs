namespace Whiteboard.LicenseServer.Data;

/// <summary>
/// Выданный пробный период. Хранится вечно — именно эта запись не даёт
/// взять три дня повторно после переустановки приложения.
/// </summary>
public class TrialActivation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string HardwareId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public bool IsActive(DateTime now) => now < ExpiresAt;
}
