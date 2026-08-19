using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Whiteboard.LicenseServer.Configuration;

namespace Whiteboard.LicenseServer.Services;

public interface IEmailSender
{
    /// <summary>Отправляет письмо с ключом. false — письмо не ушло, нужно повторить.</summary>
    Task<bool> SendLicenseKeyAsync(string email, string key, CancellationToken cancellationToken);
}

/// <summary>
/// Отправка через SendGrid v3. Обращаемся к HTTP API напрямую: нужен ровно
/// один запрос, отдельная зависимость ради него не окупается.
/// </summary>
public sealed class SendGridEmailSender : IEmailSender
{
    private const string Endpoint = "https://api.sendgrid.com/v3/mail/send";

    private readonly HttpClient _http;
    private readonly EmailOptions _email;
    private readonly LicenseOptions _license;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(
        HttpClient http,
        EmailOptions email,
        LicenseOptions license,
        ILogger<SendGridEmailSender> logger)
    {
        _http = http;
        _email = email;
        _license = license;
        _logger = logger;

        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<bool> SendLicenseKeyAsync(string email, string key, CancellationToken cancellationToken)
    {
        var body = new
        {
            personalizations = new[]
            {
                new { to = new[] { new { email } } }
            },
            from = new { email = _email.FromEmail, name = _email.FromName },
            subject = _email.Subject,
            content = new[]
            {
                new { type = "text/plain", value = EmailTemplate.BuildPlainText(key, _license) },
                new { type = "text/html", value = EmailTemplate.Build(key, _license) }
            }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _email.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("SendGrid отказал: {Status} {Details}", (int)response.StatusCode, details);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось отправить письмо с ключом.");
            return false;
        }
    }
}

/// <summary>
/// Заглушка для локальной разработки: письмо не уходит никуда, ключ пишется
/// в лог. В боевом режиме сервис без SENDGRID_API_KEY просто не стартует.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendLicenseKeyAsync(string email, string key, CancellationToken cancellationToken)
    {
        _logger.LogWarning("SendGrid не настроен. Письмо не отправлено: {Email} получил бы ключ {Key}.", email, key);
        return Task.FromResult(true);
    }
}
