namespace SchoolPi.Tariffs.Configuration;

/// <summary>
/// Настройки сервиса тарифов основного сайта (school-pi.online).
///
/// Сервис маленький намеренно: он не ведёт своих пользователей и не
/// знает про пароли и почту — этим занимается основной сайт (Flask,
/// репозиторий my_portfolio_project). Отсюда сайт получает только два
/// ответа: «какая подписка сейчас действует» и «ссылка на оплату».
/// Токены и бесплатные попытки считает сам сайт по своей же таблице
/// расхода ИИ — этому сервису про них знать незачем.
/// </summary>
public sealed record TariffOptions
{
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Общий секрет между этим сервисом и основным сайтом. Проверяется
    /// заголовком X-Api-Key на всём, кроме /health, /plans и
    /// /robokassa/result — тот защищён собственной подписью Робокассы,
    /// а не этим ключом: Робокасса не умеет слать произвольные заголовки.
    /// </summary>
    public required string ApiKey { get; init; }

    public required PaymentOptions Payments { get; init; }

    public static TariffOptions Load(IConfiguration configuration)
    {
        var options = new TariffOptions
        {
            ConnectionString = configuration.GetConnectionString("Postgres")
                               ?? Env("DATABASE_CONNECTION_STRING")
                               ?? string.Empty,

            ApiKey = First(configuration["ApiKey"], "TARIFFS_API_KEY"),

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
            }
        };

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            missing.Add("ConnectionStrings:Postgres");

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            missing.Add("TARIFFS_API_KEY");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Не заданы обязательные настройки: " + string.Join(", ", missing) +
                ". Секреты передаются через переменные окружения, см. main-site/docs/deploy.md.");
        }

        return options;
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private static string First(string? fromConfiguration, string environmentVariable)
        => !string.IsNullOrWhiteSpace(fromConfiguration)
            ? fromConfiguration.Trim()
            : Env(environmentVariable)?.Trim() ?? string.Empty;

    private static bool Bool(string? value, bool fallback)
        => bool.TryParse(value, out var parsed) ? parsed : fallback;
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

/// <summary>
/// Тарифы ИИ-функций основного сайта. Цена и ключ тарифа — источник
/// истины здесь (это деньги), лимит токенов — источник истины в
/// my_app/billing.py на сайте (это лимит, который считает сайт сам по
/// своей же таблице расхода). Ключи тарифов должны совпадать в обоих
/// местах, иначе сайт не поймёт статус оплаченной подписки.
/// </summary>
public sealed record TariffPlan(string Key, string Title, decimal Price);

public static class TariffPlans
{
    public static readonly IReadOnlyList<TariffPlan> All = new[]
    {
        new TariffPlan("tutor_basic", "Базовый", 290m),
        new TariffPlan("tutor_standard", "Стандарт", 590m),
        new TariffPlan("tutor_pro", "Профи", 990m),
        new TariffPlan("student_plus", "Ученик+", 150m),
    };

    public static TariffPlan? Find(string key) => All.FirstOrDefault(plan => plan.Key == key);
}
