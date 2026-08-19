using System.Text.Json;
using SchoolPiBoard.Online.Configuration;

namespace SchoolPiBoard.Online.Services;

public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken);
}

/// <summary>
/// Yandex SmartCaptcha — из доступных в России вариантов самый простой:
/// один GET на проверку и никакой обвязки. Ключ сайта живёт на фронтенде,
/// серверный ключ — здесь.
/// </summary>
public sealed class YandexSmartCaptchaVerifier : ICaptchaVerifier
{
    private const string Endpoint = "https://smartcaptcha.yandexcloud.net/validate";

    private readonly HttpClient _http;
    private readonly CaptchaOptions _options;
    private readonly ILogger<YandexSmartCaptchaVerifier> _logger;

    public YandexSmartCaptchaVerifier(HttpClient http, CaptchaOptions options, ILogger<YandexSmartCaptchaVerifier> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var url = $"{Endpoint}?secret={Uri.EscapeDataString(_options.SecretKey)}&token={Uri.EscapeDataString(token)}";
        if (!string.IsNullOrWhiteSpace(clientIp))
            url += $"&ip={Uri.EscapeDataString(clientIp)}";

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SmartCaptcha ответила {Status}: {Body}", (int)response.StatusCode, body);
                return false;
            }

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("status", out var status)
                   && status.GetString() == "ok";
        }
        catch (Exception ex)
        {
            // Капча недоступна — пропускать всех подряд нельзя, это открыло бы
            // регистрацию роботам ровно в тот момент, когда защита сломалась.
            _logger.LogError(ex, "Не удалось проверить капчу.");
            return false;
        }
    }
}

/// <summary>Заглушка для разработки: проверка не выполняется.</summary>
public sealed class DisabledCaptchaVerifier : ICaptchaVerifier
{
    public Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
