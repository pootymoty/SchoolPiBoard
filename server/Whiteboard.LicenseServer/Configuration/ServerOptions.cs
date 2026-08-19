using System.Globalization;

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
    public required RobokassaOptions Robokassa { get; init; }
    public required TrialOptions Trial { get; init; }
    public required WebOptions Web { get; init; }

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
            },

            Robokassa = new RobokassaOptions
            {
                MerchantLogin = configuration["Robokassa:MerchantLogin"] ?? string.Empty,
                Password1 = First(configuration["Robokassa:Password1"], "ROBOKASSA_PASSWORD1"),
                Password2 = First(configuration["Robokassa:Password2"], "ROBOKASSA_PASSWORD2"),
                Amount = ReadDecimal(configuration["Robokassa:Amount"], 15000m),
                Description = configuration["Robokassa:Description"] ?? "Лицензия Whiteboard (бессрочная)",
                PaymentUrl = configuration["Robokassa:PaymentUrl"]
                             ?? "https://auth.robokassa.ru/Merchant/Index.aspx",
                IsTest = ReadBool(configuration["Robokassa:IsTest"], false),
                SendReceipt = ReadBool(configuration["Robokassa:SendReceipt"], false),
                TaxSystem = configuration["Robokassa:TaxSystem"] ?? "npd",
                Tax = configuration["Robokassa:Tax"] ?? "none"
            },

            Trial = new TrialOptions
            {
                Days = ReadInt(configuration["Trial:Days"], 3),
                OneTrialPerEmail = ReadBool(configuration["Trial:OneTrialPerEmail"], true)
            },

            Web = new WebOptions
            {
                AllowedOrigins = configuration.GetSection("Web:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>(),
                SiteUrl = configuration["Web:SiteUrl"] ?? string.Empty
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

    private static decimal ReadDecimal(string? value, decimal fallback)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;

    private static int ReadInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;

    private static bool ReadBool(string? value, bool fallback)
        => bool.TryParse(value, out var parsed) ? parsed : fallback;

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

/// <summary>
/// Робокасса. Пароли — секреты и приходят из окружения; логин магазина
/// и цена лежат в конфиге, потому что не секретны и меняются осознанно.
/// </summary>
public sealed class RobokassaOptions
{
    public required string MerchantLogin { get; init; }
    public required string Password1 { get; init; }
    public required string Password2 { get; init; }

    /// <summary>Цена лицензии в рублях.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Назначение платежа — видно покупателю и попадает в чек.</summary>
    public required string Description { get; init; }

    public required string PaymentUrl { get; init; }

    /// <summary>Тестовый режим Робокассы: деньги не списываются.</summary>
    public required bool IsTest { get; init; }

    /// <summary>
    /// Передавать ли состав чека параметром Receipt. Для самозанятого чек
    /// обычно формирует сама Робокасса, поэтому по умолчанию выключено —
    /// включать только если фискализация с составом чека включена в кабинете.
    /// </summary>
    public required bool SendReceipt { get; init; }

    /// <summary>Система налогообложения в чеке (для самозанятого — npd).</summary>
    public required string TaxSystem { get; init; }

    /// <summary>Ставка НДС в чеке (для самозанятого — none).</summary>
    public required string Tax { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(MerchantLogin) &&
        !string.IsNullOrWhiteSpace(Password1) &&
        !string.IsNullOrWhiteSpace(Password2);
}

public sealed class TrialOptions
{
    /// <summary>Длительность пробного периода в днях.</summary>
    public required int Days { get; init; }

    /// <summary>
    /// Считать ли повтором пробный период с той же почтой на другом компьютере.
    /// Отсекает переустановку Windows и смену диска.
    /// </summary>
    public required bool OneTrialPerEmail { get; init; }
}

public sealed class WebOptions
{
    /// <summary>Домены сайта, которым разрешены запросы из браузера (CORS).</summary>
    public required string[] AllowedOrigins { get; init; }

    /// <summary>Адрес страницы покупки — используется в сообщениях об ошибке.</summary>
    public required string SiteUrl { get; init; }
}
