namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// Минимальная проверка почты. Полная валидация по RFC бессмысленна:
/// единственное настоящее подтверждение адреса — дошедшее письмо.
/// </summary>
public static class EmailAddress
{
    public const int MaxLength = 254;

    public static string? Normalize(string? email)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            return null;

        var at = value.IndexOf('@');
        if (at <= 0 || at == value.Length - 1)
            return null;

        // Второй «собаки» быть не должно, пробелов — тоже.
        if (value.IndexOf('@', at + 1) >= 0)
            return null;

        foreach (var symbol in value)
        {
            if (char.IsWhiteSpace(symbol) || symbol == ',' || symbol == ';')
                return null;
        }

        // В домене должна быть точка: «user@localhost» покупателю письмо не принесёт.
        if (!value[(at + 1)..].Contains('.'))
            return null;

        return value.ToLowerInvariant();
    }
}
