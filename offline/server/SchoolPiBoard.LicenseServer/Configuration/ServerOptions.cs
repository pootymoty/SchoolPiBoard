using System.Globalization;

namespace SchoolPiBoard.LicenseServer.Configuration;

/// <summary>
/// Настройки сервиса. Всё, что является секретом, приходит из переменных
/// окружения — в appsettings.json лежат только пустые заглушки.
/// </summary>
public sealed class ServerOptions
{
    public required string ConnectionString { get; init; }
    public required LicenseOptions License { get; init; }
    public required SmtpOptions Smtp { get; init; }
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

            Smtp = new SmtpOptions
            {
                Host = configuration["Smtp:Host"] ?? string.Empty,
                Port = ReadInt(configuration["Smtp:Port"], 465),
                User = configuration["Smtp:User"] ?? string.Empty,
                Password = First(configuration["Smtp:Password"], "SMTP_PASSWORD"),
                FromEmail = configuration["Smtp:FromEmail"] ?? string.Empty,
                FromName = configuration["Smtp:FromName"] ?? "SchoolPiBoard",
                Subject = configuration["Smtp:Subject"] ?? "Ваш ключ SchoolPiBoard",
                // Яндекс 360 отдаёт SMTP на 465 через SSL; 587 со STARTTLS —
                // второй рабочий вариант, если 465 закрыт на сервере.
                UseStartTls = ReadBool(configuration["Smtp:UseStartTls"], false)
            },

            Robokassa = new RobokassaOptions
            {
                MerchantLogin = configuration["Robokassa:MerchantLogin"] ?? string.Empty,
                Password1 = First(configuration["Robokassa:Password1"], "ROBOKASSA_PASSWORD1"),
                Password2 = First(configuration["Robokassa:Password2"], "ROBOKASSA_PASSWORD2"),
                Amount = ReadDecimal(configuration["Robokassa:Amount"], 2999m),
                Description = configuration["Robokassa:Description"] ?? "Лицензия SchoolPiBoard (бессрочная)",
                PaymentUrl = configuration["Robokassa:PaymentUrl"]
                             ?? "https://auth.robokassa.ru/Merchant/Index.aspx",
                IsTest = ReadBool(configuration["Robokassa:IsTest"], false),
                SendReceipt = ReadBool(configuration["Robokassa:SendReceipt"], true),
                TaxSystem = configuration["Robokassa:TaxSystem"] ?? string.Empty,
                Tax = configuration["Robokassa:Tax"] ?? "none",
                PaymentObject = configuration["Robokassa:PaymentObject"] ?? "service"
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
            // В разработке письма пишутся в лог — в бою так работать нельзя.
            if (!options.Smtp.IsConfigured)
                missing.Add("Smtp:Host / Smtp:FromEmail");
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

/// <summary>Почта, с которой уходит письмо с ключом.</summary>
public sealed class SmtpOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public required string FromEmail { get; init; }
    public required string FromName { get; init; }
    public required string Subject { get; init; }

    /// <summary>false — SSL сразу (порт 465), true — STARTTLS (порт 587).</summary>
    public required bool UseStartTls { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromEmail);
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
    /// Передавать ли состав чека параметром Receipt. Нужно и самозанятому:
    /// без Receipt Робокасса отвечает «вы не передали нам состав чека»
    /// и предлагает выписать чек вручную. Проверено на живой оплате.
    /// </summary>
    public required bool SendReceipt { get; init; }

    /// <summary>
    /// Система налогообложения в чеке. Робокасса принимает osn, usn_income,
    /// usn_income_outcome, envd, esn, patent — режима НПД в этом перечне нет.
    /// Пустое значение означает «не передавать поле»: настройка берётся
    /// из кабинета магазина.
    /// </summary>
    public required string TaxSystem { get; init; }

    /// <summary>Ставка НДС в чеке (для самозанятого — none).</summary>
    public required string Tax { get; init; }

    /// <summary>
    /// Признак предмета расчёта. По смыслу оферты подошло бы
    /// intellectual_activity — «предоставление прав на результаты
    /// интеллектуальной деятельности», но принимает ли его Робокасса,
    /// на живой оплате пока не проверено, а каждая проверка стоит денег.
    /// Поэтому по умолчанию service: он точно проходит. Менять — только
    /// после успешной пробной оплаты на малую сумму.
    /// </summary>
    public required string PaymentObject { get; init; }

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
