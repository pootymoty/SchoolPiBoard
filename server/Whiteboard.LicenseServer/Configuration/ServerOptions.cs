namespace Whiteboard.LicenseServer.Configuration;

/// <summary>
/// Настройки сервиса. Всё, что является секретом, приходит из переменных
/// окружения — в appsettings.json лежат только пустые заглушки.
/// </summary>
public sealed class ServerOptions
{
    public required string ConnectionString { get; init; }
    public required LicenseOptions License { get; init; }
    public required StripeOptions Stripe { get; init; }
    public required EmailOptions Email { get; init; }

    /// <summary>
    /// Собирает настройки и сразу проверяет их. В боевом режиме отсутствие
    /// секрета — повод не стартовать вовсе: молча работающий сервис,
    /// который не умеет отправлять письма, хуже упавшего.
    /// </summary>
    public static ServerOptions Load(IConfiguration configuration, bool development)
    {
        var options = new ServerOptions
        {
            ConnectionString =
                configuration.GetConnectionString("Postgres")
                ?? Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
                ?? string.Empty,

            License = new LicenseOptions
            {
                TokenSecret = First(configuration["License:TokenSecret"], "LICENSE_TOKEN_SECRET"),
                DeviceLimit = int.TryParse(configuration["License:DeviceLimit"], out var limit) && limit > 0
                    ? limit
                    : 2,
                DownloadUrl = configuration["License:DownloadUrl"] ?? string.Empty,
                SupportEmail = configuration["License:SupportEmail"] ?? string.Empty
            },

            Stripe = new StripeOptions
            {
                WebhookSecret = First(configuration["Stripe:WebhookSecret"], "STRIPE_WEBHOOK_SECRET")
            },

            Email = new EmailOptions
            {
                ApiKey = First(configuration["SendGrid:ApiKey"], "SENDGRID_API_KEY"),
                FromEmail = configuration["SendGrid:FromEmail"] ?? string.Empty,
                FromName = configuration["SendGrid:FromName"] ?? "Whiteboard",
                Subject = configuration["SendGrid:Subject"] ?? "Ваш ключ Whiteboard"
            }
        };

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            missing.Add("ConnectionStrings:Postgres");

        if (string.IsNullOrWhiteSpace(options.License.TokenSecret))
            missing.Add("LICENSE_TOKEN_SECRET");

        if (!development)
        {
            // В разработке без этих значений сервис поднимается: письма пишутся
            // в лог, а вебхук принимается без проверки подписи.
            if (string.IsNullOrWhiteSpace(options.Stripe.WebhookSecret))
                missing.Add("STRIPE_WEBHOOK_SECRET");

            if (string.IsNullOrWhiteSpace(options.Email.ApiKey))
                missing.Add("SENDGRID_API_KEY");

            if (string.IsNullOrWhiteSpace(options.Email.FromEmail))
                missing.Add("SendGrid:FromEmail");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Не заданы обязательные настройки: " + string.Join(", ", missing) +
                ". Секреты передаются через переменные окружения, см. server/README.md.");
        }

        return options;
    }

    private static string First(string? fromConfiguration, string environmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(fromConfiguration))
            return fromConfiguration.Trim();

        return Environment.GetEnvironmentVariable(environmentVariable)?.Trim() ?? string.Empty;
    }
}

public sealed class LicenseOptions
{
    /// <summary>Секрет подписи токенов активации (HMAC-SHA256).</summary>
    public required string TokenSecret { get; init; }

    /// <summary>Сколько устройств разрешено на один ключ.</summary>
    public required int DeviceLimit { get; init; }

    /// <summary>Ссылка на скачивание EXE — попадает в письмо.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Адрес поддержки для письма.</summary>
    public required string SupportEmail { get; init; }
}

public sealed class StripeOptions
{
    public required string WebhookSecret { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(WebhookSecret);
}

public sealed class EmailOptions
{
    public required string ApiKey { get; init; }
    public required string FromEmail { get; init; }
    public required string FromName { get; init; }
    public required string Subject { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromEmail);
}
