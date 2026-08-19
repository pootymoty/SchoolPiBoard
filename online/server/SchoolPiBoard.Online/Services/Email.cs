using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SchoolPiBoard.Online.Configuration;

namespace SchoolPiBoard.Online.Services;

public interface IEmailSender
{
    /// <summary>false — письмо не ушло. Вызывающий решает, ошибка это или повод повторить.</summary>
    Task<bool> SendAsync(string to, string subject, string html, string text, CancellationToken cancellationToken);
}

/// <summary>
/// Отправка через обычный SMTP — почта своего домена, без внешних сервисов
/// рассылки. Для писем-подтверждений этого достаточно, а зависимость всего
/// одна и работает в России без оговорок.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(SmtpOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string subject, string html, string text, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            var security = _options.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.SslOnConnect;

            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.User))
                await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            // Адрес получателя в лог не пишем целиком: это персональные данные.
            _logger.LogError(ex, "Не удалось отправить письмо «{Subject}».", subject);
            return false;
        }
    }
}

/// <summary>Заглушка для разработки: письмо целиком уходит в лог.</summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<bool> SendAsync(string to, string subject, string html, string text, CancellationToken cancellationToken)
    {
        _logger.LogWarning("SMTP не настроен. Письмо для {To} — «{Subject}»:\n{Text}", to, subject, text);
        return Task.FromResult(true);
    }
}

/// <summary>Тексты писем. HTML простой, без картинок и внешних ресурсов.</summary>
public static class EmailTemplates
{
    public static (string Subject, string Html, string Text) ConfirmRegistration(string link, int ttlMinutes)
    {
        var subject = "Подтвердите регистрацию на SchoolPiBoard";

        var text =
            $"""
             Здравствуйте!

             Чтобы завершить регистрацию на SchoolPiBoard, откройте ссылку:
             {link}

             Ссылка действует {ttlMinutes} минут. Если не успеете — регистрацию
             нужно будет пройти заново.

             Если вы не регистрировались, просто удалите это письмо.
             """;

        return (subject, Wrap("Подтвердите регистрацию", $"""
            <p>Чтобы завершить регистрацию на SchoolPiBoard, нажмите кнопку.</p>
            {Button(link, "Подтвердить почту")}
            <p style="color:#61636e;font-size:13px;">
              Ссылка действует {ttlMinutes} минут. Если не успеете — регистрацию нужно будет пройти заново.
              Если вы не регистрировались, просто удалите это письмо.
            </p>
            """), text);
    }

    public static (string Subject, string Html, string Text) ConfirmAccountDeletion(string link, int ttlMinutes)
    {
        var subject = "Удаление учётной записи SchoolPiBoard";

        var text =
            $"""
             Вы запросили удаление учётной записи SchoolPiBoard.

             Подтвердите удаление по ссылке:
             {link}

             Ссылка действует {ttlMinutes} минут. Вместе с учётной записью будут
             удалены ваши доски и отменена подписка — отменить это будет нельзя.

             Если вы этого не запрашивали, ничего не делайте и смените пароль.
             """;

        return (subject, Wrap("Удаление учётной записи", $"""
            <p>Вы запросили удаление учётной записи SchoolPiBoard.</p>
            {Button(link, "Подтвердить удаление")}
            <p style="color:#61636e;font-size:13px;">
              Ссылка действует {ttlMinutes} минут. Вместе с учётной записью будут удалены ваши
              доски и отменена подписка — вернуть их будет нельзя.
              Если вы этого не запрашивали, ничего не делайте и смените пароль.
            </p>
            """), text);
    }

    public static (string Subject, string Html, string Text) PasswordChanged()
    {
        var subject = "Пароль на SchoolPiBoard изменён";

        var text =
            """
            Пароль от вашей учётной записи SchoolPiBoard только что изменён.

            Если это были не вы — восстановите доступ и напишите нам.
            """;

        return (subject, Wrap("Пароль изменён", """
            <p>Пароль от вашей учётной записи SchoolPiBoard только что изменён.</p>
            <p style="color:#61636e;font-size:13px;">Если это были не вы — восстановите доступ и напишите нам.</p>
            """), text);
    }

    public static (string Subject, string Html, string Text) SubscriptionActivated(string until)
    {
        var subject = "Подписка SchoolPiBoard активна";

        var text =
            $"""
             Спасибо за оплату. Подписка активна до {until}.

             Управлять подпиской можно в настройках профиля.
             """;

        return (subject, Wrap("Подписка активна", $"""
            <p>Спасибо за оплату. Подписка активна до <b>{until}</b>.</p>
            <p style="color:#61636e;font-size:13px;">Управлять подпиской можно в настройках профиля.</p>
            """), text);
    }

    private static string Button(string link, string caption) => $"""
        <p style="margin:22px 0;">
          <a href="{link}" style="display:inline-block;padding:12px 24px;background:#5b6cf7;
             color:#ffffff;border-radius:8px;text-decoration:none;font-weight:600;">{caption}</a>
        </p>
        <p style="color:#61636e;font-size:12px;word-break:break-all;">{link}</p>
        """;

    private static string Wrap(string title, string body) => $"""
        <!DOCTYPE html>
        <html lang="ru">
          <body style="margin:0;padding:24px;background:#f4f5f8;font-family:Segoe UI,Arial,sans-serif;color:#1f2026;">
            <div style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:12px;padding:28px;">
              <h1 style="margin:0 0 16px;font-size:20px;">{title}</h1>
              {body}
              <p style="margin-top:26px;color:#9a9ba5;font-size:12px;">SchoolPiBoard</p>
            </div>
          </body>
        </html>
        """;
}
