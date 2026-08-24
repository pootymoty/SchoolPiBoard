using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SchoolPiBoard.LicenseServer.Configuration;

namespace SchoolPiBoard.LicenseServer.Services;

public interface IEmailSender
{
    /// <summary>Отправляет письмо с ключом. false — письмо не ушло, нужно повторить.</summary>
    Task<bool> SendLicenseKeyAsync(string email, string key, CancellationToken cancellationToken);
}

/// <summary>
/// Отправка через обычный SMTP — почта своего домена, без внешних сервисов
/// рассылки. Для одного письма на покупку этого достаточно, а работает
/// это везде и без оговорок.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _smtp;
    private readonly LicenseOptions _license;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(SmtpOptions smtp, LicenseOptions license, ILogger<SmtpEmailSender> logger)
    {
        _smtp = smtp;
        _license = license;
        _logger = logger;
    }

    public async Task<bool> SendLicenseKeyAsync(string email, string key, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.FromEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = _smtp.Subject;

        message.Body = new BodyBuilder
        {
            HtmlBody = EmailTemplate.Build(key, _license),
            TextBody = EmailTemplate.BuildPlainText(key, _license)
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            // 465 — SSL сразу при подключении, 587 — обычное соединение
            // с переходом на TLS. Яндекс 360 поддерживает оба.
            var security = _smtp.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.SslOnConnect;

            await client.ConnectAsync(_smtp.Host, _smtp.Port, security, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_smtp.User))
                await client.AuthenticateAsync(_smtp.User, _smtp.Password, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            // Ключ покупателя в лог не пишем: письмо не ушло, но сам ключ
            // уже в базе, и повтор доставки его оттуда возьмёт.
            _logger.LogError(ex, "Не удалось отправить письмо с ключом.");
            return false;
        }
    }
}

/// <summary>
/// Заглушка для локальной разработки: письмо не уходит никуда, ключ пишется
/// в лог. В боевом режиме сервис без настроенного SMTP не стартует.
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
        _logger.LogWarning("SMTP не настроен. Письмо не отправлено: {Email} получил бы ключ {Key}.", email, key);
        return Task.FromResult(true);
    }
}
