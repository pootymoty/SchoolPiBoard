using System.Net;
using Whiteboard.LicenseServer.Configuration;

namespace Whiteboard.LicenseServer.Services;

/// <summary>Письмо с ключом. Простой HTML без картинок и внешних ресурсов.</summary>
public static class EmailTemplate
{
    public static string Build(string key, LicenseOptions options)
    {
        var safeKey = WebUtility.HtmlEncode(key);
        var downloadBlock = string.IsNullOrWhiteSpace(options.DownloadUrl)
            ? string.Empty
            : $"""
                   <p style="margin:0 0 18px;">
                     Скачать программу:
                     <a href="{WebUtility.HtmlEncode(options.DownloadUrl)}" style="color:#5b6cf7;">{WebUtility.HtmlEncode(options.DownloadUrl)}</a>
                   </p>
               """;

        var supportBlock = string.IsNullOrWhiteSpace(options.SupportEmail)
            ? string.Empty
            : $"""
                   <p style="margin:0;color:#6a6a78;font-size:13px;">
                     Вопросы по лицензии: {WebUtility.HtmlEncode(options.SupportEmail)}
                   </p>
               """;

        return $"""
            <!DOCTYPE html>
            <html lang="ru">
              <body style="margin:0;padding:24px;background:#f3f3f6;font-family:Segoe UI,Arial,sans-serif;color:#1f1f26;">
                <div style="max-width:520px;margin:0 auto;background:#ffffff;border-radius:12px;padding:28px;">
                  <h1 style="margin:0 0 16px;font-size:20px;">Спасибо за покупку Whiteboard</h1>

                  <p style="margin:0 0 12px;">Ваш лицензионный ключ:</p>

                  <p style="margin:0 0 22px;font-size:22px;letter-spacing:2px;font-family:Consolas,monospace;
                            background:#f3f3f6;border-radius:8px;padding:14px;text-align:center;">
                    {safeKey}
                  </p>

            {downloadBlock}

                  <p style="margin:0 0 8px;font-weight:600;">Как активировать</p>
                  <ol style="margin:0 0 18px;padding-left:20px;line-height:1.6;">
                    <li>Запустите Whiteboard.</li>
                    <li>На экране активации введите ключ и нажмите «Активировать».</li>
                    <li>Готово — дальше программа работает без интернета.</li>
                  </ol>

                  <p style="margin:0 0 18px;color:#6a6a78;font-size:13px;">
                    Один ключ рассчитан на {options.DeviceLimit} компьютера одновременно.
                    Перед переездом на новый компьютер отвяжите старый:
                    Настройки → «О программе и лицензии» → «Отвязать этот компьютер».
                  </p>

            {supportBlock}
                </div>
              </body>
            </html>
            """;
    }

    public static string BuildPlainText(string key, LicenseOptions options)
    {
        var lines = new List<string>
        {
            "Спасибо за покупку Whiteboard.",
            string.Empty,
            "Ваш лицензионный ключ: " + key,
            string.Empty
        };

        if (!string.IsNullOrWhiteSpace(options.DownloadUrl))
        {
            lines.Add("Скачать программу: " + options.DownloadUrl);
            lines.Add(string.Empty);
        }

        lines.Add("Как активировать: запустите Whiteboard, введите ключ на экране активации и нажмите «Активировать».");
        lines.Add($"Один ключ рассчитан на {options.DeviceLimit} компьютера одновременно.");

        if (!string.IsNullOrWhiteSpace(options.SupportEmail))
        {
            lines.Add(string.Empty);
            lines.Add("Вопросы по лицензии: " + options.SupportEmail);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
