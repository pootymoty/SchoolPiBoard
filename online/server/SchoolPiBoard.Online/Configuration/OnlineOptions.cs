using System.Globalization;

namespace SchoolPiBoard.Online.Configuration;

/// <summary>
/// Настройки сервиса онлайн-доски. Секреты приходят из переменных окружения,
/// в appsettings.json лежат только заглушки.
/// </summary>
public sealed record OnlineOptions
{
    public required string ConnectionString { get; init; }
    public required AuthOptions Auth { get; init; }
    public required SiteOptions Site { get; init; }
    public required SmtpOptions Smtp { get; init; }
    public required CaptchaOptions Captcha { get; init; }
    public required PaymentOptions Payments { get; init; }
    public required InviteOptions Invites { get; init; }
    public required RedisOptions Redis { get; init; }

    public static OnlineOptions Load(IConfiguration configuration, bool development)
    {
        var options = new OnlineOptions
        {
            ConnectionString = configuration.GetConnectionString("Postgres")
                               ?? Env("DATABASE_CONNECTION_STRING")
                               ?? string.Empty,

            Auth = new AuthOptions
            {
                TokenSecret = First(configuration["Auth:TokenSecret"], "AUTH_TOKEN_SECRET"),
                Issuer = configuration["Auth:Issuer"] ?? "school-pi-board",
                Audience = configuration["Auth:Audience"] ?? "school-pi-board",
                TokenLifetimeDays = Int(configuration["Auth:TokenLifetimeDays"], 30),
                RegistrationTtlMinutes = Int(configuration["Auth:RegistrationTtlMinutes"], 60)
            },

            Site = new SiteOptions
            {
                BaseUrl = (configuration["Site:BaseUrl"] ?? "https://school-pi-board.online").TrimEnd('/'),
                SupportEmail = configuration["Site:SupportEmail"] ?? string.Empty,
                AppOrigins = configuration.GetSection("Site:AppOrigins").Get<string[]>() ?? Array.Empty<string>()
            },

            Smtp = new SmtpOptions
            {
                Host = configuration["Smtp:Host"] ?? string.Empty,
                Port = Int(configuration["Smtp:Port"], 465),
                User = configuration["Smtp:User"] ?? string.Empty,
                Password = First(configuration["Smtp:Password"], "SMTP_PASSWORD"),
                FromEmail = configuration["Smtp:FromEmail"] ?? string.Empty,
                FromName = configuration["Smtp:FromName"] ?? "SchoolPiBoard",
                // Яндекс 360 отдаёт SMTP на 465 через SSL; 587 со STARTTLS —
                // второй поддерживаемый вариант, если 465 закрыт на сервере.
                UseStartTls = Bool(configuration["Smtp:UseStartTls"], false)
            },

            Captcha = new CaptchaOptions
            {
                Provider = (configuration["Captcha:Provider"] ?? "disabled").Trim().ToLowerInvariant(),
                SecretKey = First(configuration["Captcha:SecretKey"], "CAPTCHA_SECRET_KEY")
            },

            Payments = new PaymentOptions
            {
                MerchantLogin = configuration["Payments:MerchantLogin"] ?? string.Empty,
                Password1 = First(configuration["Payments:Password1"], "ROBOKASSA_PASSWORD1"),
                Password2 = First(configuration["Payments:Password2"], "ROBOKASSA_PASSWORD2"),
                PaymentUrl = configuration["Payments:PaymentUrl"] ?? "https://auth.robokassa.ru/Merchant/Index.aspx",
                IsTest = Bool(configuration["Payments:IsTest"], false),
                SendReceipt = Bool(configuration["Payments:SendReceipt"], false),
                TaxSystem = configuration["Payments:TaxSystem"] ?? "npd",
                Tax = configuration["Payments:Tax"] ?? "none"
            },

            Invites = new InviteOptions
            {
                LinkLifetimeDays = Int(configuration["Invites:LinkLifetimeDays"], 7),
                MemberEditorDays = Int(configuration["Invites:MemberEditorDays"], 30)
            },

            Redis = new RedisOptions
            {
                ConnectionString = First(configuration["Redis:ConnectionString"], "REDIS_CONNECTION_STRING")
            }
        };

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            missing.Add("ConnectionStrings:Postgres");

        if (string.IsNullOrWhiteSpace(options.Auth.TokenSecret))
            missing.Add("AUTH_TOKEN_SECRET");

        if (!development)
        {
            // В разработке письма пишутся в лог, а присутствие держится
            // в памяти одного процесса — в бою ни то, ни другое не годится.
            //
            // Капчи в этом списке нет намеренно: Yandex SmartCaptcha пока
            // недоступна для подключения, поэтому сервис поднимается и без
            // неё, но пишет об этом предупреждение при старте.
            if (!options.Smtp.IsConfigured)
                missing.Add("Smtp:Host / Smtp:FromEmail");

            if (string.IsNullOrWhiteSpace(options.Redis.ConnectionString))
                missing.Add("REDIS_CONNECTION_STRING");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Не заданы обязательные настройки: " + string.Join(", ", missing) +
                ". Секреты передаются через переменные окружения, см. online/server/README.md.");
        }

        return options;
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private static string First(string? fromConfiguration, string environmentVariable)
        => !string.IsNullOrWhiteSpace(fromConfiguration)
            ? fromConfiguration.Trim()
            : Env(environmentVariable)?.Trim() ?? string.Empty;

    private static int Int(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;

    private static bool Bool(string? value, bool fallback)
        => bool.TryParse(value, out var parsed) ? parsed : fallback;
}

public sealed record AuthOptions
{
    public required string TokenSecret { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required int TokenLifetimeDays { get; init; }

    /// <summary>Сколько живёт ссылка подтверждения регистрации. По ТЗ — час.</summary>
    public required int RegistrationTtlMinutes { get; init; }
}

public sealed record SiteOptions
{
    /// <summary>Адрес сайта — из него собираются ссылки в письмах.</summary>
    public required string BaseUrl { get; init; }

    public required string SupportEmail { get; init; }

    /// <summary>Домены, которым браузер разрешает обращаться к API.</summary>
    public required string[] AppOrigins { get; init; }
}

public sealed record SmtpOptions
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public required string FromEmail { get; init; }
    public required string FromName { get; init; }
    public required bool UseStartTls { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromEmail);
}

public sealed record CaptchaOptions
{
    /// <summary>«yandex» — Yandex SmartCaptcha, «disabled» — проверки нет (только разработка).</summary>
    public required string Provider { get; init; }

    public required string SecretKey { get; init; }
}

public sealed record PaymentOptions
{
    public required string MerchantLogin { get; init; }
    public required string Password1 { get; init; }
    public required string Password2 { get; init; }
    public required string PaymentUrl { get; init; }
    public required bool IsTest { get; init; }
    public required bool SendReceipt { get; init; }
    public required string TaxSystem { get; init; }
    public required string Tax { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(MerchantLogin) &&
        !string.IsNullOrWhiteSpace(Password1) &&
        !string.IsNullOrWhiteSpace(Password2);
}

public sealed record InviteOptions
{
    /// <summary>
    /// Сколько дней по ссылке можно войти на доску. Потом ссылка перестаёт
    /// работать, но у тех, кто успел войти, доступ остаётся.
    /// </summary>
    public required int LinkLifetimeDays { get; init; }

    /// <summary>
    /// Сколько дней приглашённый участник может менять доску. Дальше он
    /// становится наблюдателем, и вернуть ему право правки может только
    /// владелец — так ни разошедшаяся ссылка, ни забытое приглашение
    /// не дают вечного доступа на редактирование.
    /// </summary>
    public required int MemberEditorDays { get; init; }
}

public sealed record RedisOptions
{
    public required string ConnectionString { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

/// <summary>Тарифы подписки. Цены в рублях, срок в днях.</summary>
public sealed record SubscriptionPlan(int Days, decimal Price, string Title);

public static class SubscriptionPlans
{
    public const int TrialDays = 7;

    public static readonly IReadOnlyList<SubscriptionPlan> All = new[]
    {
        new SubscriptionPlan(30, 499m, "30 дней"),
        new SubscriptionPlan(90, 1449m, "90 дней"),
        new SubscriptionPlan(180, 2799m, "180 дней"),
        new SubscriptionPlan(365, 5399m, "365 дней")
    };

    public static SubscriptionPlan? Find(int days) => All.FirstOrDefault(plan => plan.Days == days);
}
