using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Whiteboard.LicenseServer.Configuration;
using Whiteboard.LicenseServer.Data;

namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// Токены входа для веб-версии. Это не тот же токен, что выдаётся десктопной
/// лицензии: там подпись без срока действия, здесь обычный JWT со сроком,
/// и секрет у них разный.
/// </summary>
public sealed class AuthTokenService
{
    private readonly AuthOptions _options;

    public AuthTokenService(AuthOptions options)
    {
        _options = options;
    }

    public int LifetimeDays => _options.TokenLifetimeDays;

    public string Issue(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.TokenSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName),
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

    /// <summary>
    /// Параметры проверки для middleware. Статический метод, потому что нужны
    /// они при настройке приложения — до того, как соберётся контейнер.
    /// </summary>
    public static TokenValidationParameters CreateValidationParameters(AuthOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.TokenSecret)),
        ValidateLifetime = true,
        // Часы сервера и клиента расходятся редко, а пятиминутный запас
        // по умолчанию слишком щедрый для токена входа.
        ClockSkew = TimeSpan.FromSeconds(30)
    };
}

/// <summary>Чтение данных пользователя из токена.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Идентификатор пользователя. Претензии не переименовываются
    /// (MapInboundClaims=false), поэтому читаем ровно «sub».
    /// </summary>
    public static Guid? UserId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static string? UserEmail(this ClaimsPrincipal? principal)
        => principal?.FindFirst(JwtRegisteredClaimNames.Email)?.Value
           ?? principal?.FindFirst(ClaimTypes.Email)?.Value;

    public static string UserDisplayName(this ClaimsPrincipal? principal)
    {
        var name = principal?.FindFirst("name")?.Value;
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        var email = principal.UserEmail();
        if (string.IsNullOrWhiteSpace(email))
            return "Участник";

        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}
