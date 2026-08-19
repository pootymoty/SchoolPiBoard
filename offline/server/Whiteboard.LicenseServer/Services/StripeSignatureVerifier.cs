using System.Security.Cryptography;
using System.Text;

namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// Проверка заголовка Stripe-Signature. Схема Stripe: заголовок вида
/// «t=1699999999,v1=hex,v1=hex», подписывается строка «t.тело_запроса»
/// ключом вебхука по HMAC-SHA256.
/// </summary>
public static class StripeSignatureVerifier
{
    /// <summary>Максимальный возраст события — защита от повторной отправки перехваченного запроса.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public static bool Verify(string payload, string? signatureHeader, string secret, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
            return false;

        long? timestamp = null;
        var signatures = new List<string>();

        foreach (var item in signatureHeader.Split(','))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0)
                continue;

            var name = item[..separator].Trim();
            var value = item[(separator + 1)..].Trim();

            if (name == "t" && long.TryParse(value, out var parsed))
                timestamp = parsed;
            else if (name == "v1")
                signatures.Add(value);
        }

        if (timestamp is null || signatures.Count == 0)
            return false;

        var age = now - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (age.Duration() > (tolerance ?? DefaultTolerance))
            return false;

        var expected = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp.Value}.{payload}"))).ToLowerInvariant();

        var expectedBytes = Encoding.ASCII.GetBytes(expected);

        foreach (var signature in signatures)
        {
            var candidate = Encoding.ASCII.GetBytes(signature.ToLowerInvariant());
            if (CryptographicOperations.FixedTimeEquals(expectedBytes, candidate))
                return true;
        }

        return false;
    }
}
