using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SchoolPiBoard.Online.Configuration;
using SchoolPiBoard.Online.Data;

namespace SchoolPiBoard.Online.Services;

/// <summary>Хеширование паролей PBKDF2-HMAC-SHA256: «pbkdf2.итерации.соль.хеш».</summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 210_000;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, DefaultIterations, Algorithm, KeySize);

        return string.Join('.', "pbkdf2", DefaultIterations.ToString(),
            Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return false;

        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, Algorithm, expected.Length);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Одноразовые коды для ссылок в письмах и приглашений.
/// В базе лежит только хеш: утечка таблицы не даёт воспользоваться ссылкой.
/// </summary>
public static class SecurityTokens
{
    public static string Create() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashOf(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

/// <summary>Минимальная проверка почты: настоящая проверка — это дошедшее письмо.</summary>
public static class EmailAddress
{
    public const int MaxLength = 254;

    public static string? Normalize(string? email)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            return null;

        var at = value.IndexOf('@');
        if (at <= 0 || at == value.Length - 1 || value.IndexOf('@', at + 1) >= 0)
            return null;

        foreach (var symbol in value)
        {
            if (char.IsWhiteSpace(symbol) || symbol == ',' || symbol == ';')
                return null;
        }

        if (!value[(at + 1)..].Contains('.'))
            return null;

        return value.ToLowerInvariant();
    }
}

/// <summary>Токены входа.</summary>
public sealed class AuthTokenService
{
    private readonly AuthOptions _options;

    public AuthTokenService(AuthOptions options)
    {
        _options = options;
    }

    public string Issue(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.TokenSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddDays(_options.TokenLifetimeDays),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static TokenValidationParameters CreateValidationParameters(AuthOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.TokenSecret)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? UserId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static string UserDisplayName(this ClaimsPrincipal? principal)
    {
        var name = principal?.FindFirst("name")?.Value;
        return string.IsNullOrWhiteSpace(name) ? "Участник" : name;
    }
}

/// <summary>Цвет участника: курсор и рамка занятого объекта.</summary>
public static class UserColor
{
    private static readonly string[] Palette =
    {
        "#E5484D", "#E5A32B", "#46A758", "#12A594",
        "#0091FF", "#5B6CF7", "#8E4EC6", "#D6409F",
        "#3E9B4F", "#D14D41", "#0D74CE", "#AB6400"
    };

    public static string For(Guid userId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(userId.ToString()));
        return Palette[hash[0] % Palette.Length];
    }
}
