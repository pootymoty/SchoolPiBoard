namespace SchoolPiBoard.Online.Data;

/// <summary>Учётная запись. Появляется только после подтверждения почты.</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Всегда в нижнем регистре — на этом держится уникальный индекс.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public DateOnly BirthDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Когда был взят пробный период. Он даётся один раз.</summary>
    public DateTime? TrialUsedAt { get; set; }

    public string FullName => $"{LastName} {FirstName}".Trim();
}

/// <summary>
/// Заявка на регистрацию, ожидающая подтверждения почты. Если за час
/// подтверждения не пришло, запись перестаёт действовать и регистрацию
/// нужно проходить заново — так решено в техническом задании.
/// </summary>
public class PendingRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public DateOnly BirthDate { get; set; }

    /// <summary>Хеш кода из письма — сам код есть только у получателя письма.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }
}

/// <summary>Подтверждение действия по почте.</summary>
public class EmailAction
{
    public const string KindDeleteAccount = "delete_account";

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }
}
