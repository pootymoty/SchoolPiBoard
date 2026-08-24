namespace SchoolPiBoard.LicenseServer.Services;

/// <summary>Проверка отпечатка устройства, присланного клиентом.</summary>
public static class DeviceId
{
    /// <summary>Клиент присылает хеш; всё, что длиннее, приходит не от него.</summary>
    public const int MaxLength = 128;

    /// <summary>Приводит к единому виду или возвращает null, если прислали мусор.</summary>
    public static string? Normalize(string? hardwareId)
    {
        var value = hardwareId?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            return null;

        foreach (var symbol in value)
        {
            if (!char.IsLetterOrDigit(symbol) && symbol != '-' && symbol != '_')
                return null;
        }

        return value.ToUpperInvariant();
    }
}
