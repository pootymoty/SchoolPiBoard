namespace Whiteboard.LicenseServer.Data;

/// <summary>Пользователь веб-версии.</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Всегда в нижнем регистре — так работает уникальный индекс.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Имя для списка участников. Если не задано — показываем часть почты.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
