using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Whiteboard.LicenseServer.Configuration;

namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// Выдаёт клиенту подписанный токен активации: JWT (HS256) без срока
/// действия, но с полем issuedAt. Токен подписывается и проверяется одним
/// и тем же сервисом, поэтому обходимся HMAC без внешних библиотек.
/// </summary>
public sealed class TokenService
{
    private const string Issuer = "whiteboard-license";

    private readonly byte[] _secret;

    public TokenService(LicenseOptions options)
    {
        _secret = Encoding.UTF8.GetBytes(options.TokenSecret);
    }

    public string Issue(Guid licenseId, string key, string hardwareId)
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        }));

        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["sub"] = licenseId.ToString(),
            ["key"] = key,
            ["hwid"] = hardwareId,
            ["issuedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }));

        var signature = Encode(Sign($"{header}.{payload}"));
        return $"{header}.{payload}.{signature}";
    }

    /// <summary>Проверка подписи — на случай, если токен понадобится принимать обратно.</summary>
    public bool IsSignatureValid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        var expected = Encode(Sign($"{parts[0]}.{parts[1]}"));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(parts[2]));
    }

    private byte[] Sign(string value) => HMACSHA256.HashData(_secret, Encoding.ASCII.GetBytes(value));

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
