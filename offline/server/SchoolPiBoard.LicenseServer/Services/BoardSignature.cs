using System.Security.Cryptography;
using System.Text;

namespace SchoolPiBoard.LicenseServer.Services;

/// <summary>
/// Подпись запросов между доской и сервисом ключей.
///
/// Обе службы свои, стоят на одной машине за одним прокси, и городить
/// между ними взаимный TLS не за чем. Достаточно общего секрета: тело
/// запроса подписывается им, и чужой запрос без секрета подписать не
/// сможет.
///
/// В подпись входит и время: без него однажды перехваченный запрос можно
/// было бы повторять сколько угодно. Пять минут — с запасом на расхождение
/// часов между службами.
/// </summary>
public static class BoardSignature
{
    public const string TimestampHeader = "X-Timestamp";
    public const string SignatureHeader = "X-Signature";

    /// <summary>Насколько старую подпись ещё принимаем.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static string Sign(string secret, string timestamp, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes(timestamp + "." + body);

        return Convert.ToHexString(HMACSHA256.HashData(key, payload)).ToLowerInvariant();
    }

    /// <summary>
    /// Проверяет подпись и свежесть запроса.
    ///
    /// Сравнение — постоянного времени: обычное посимвольное по времени
    /// ответа выдаёт, сколько первых знаков угадано.
    /// </summary>
    public static bool Verify(string secret, string? timestamp, string? signature, string body)
    {
        if (string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!long.TryParse(timestamp, out var unix)) return false;

        var moment = DateTimeOffset.FromUnixTimeSeconds(unix);
        if ((DateTimeOffset.UtcNow - moment).Duration() > Lifetime) return false;

        var expected = Sign(secret, timestamp, body);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    public static string Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
}
