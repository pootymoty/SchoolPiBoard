namespace SchoolPi.Tariffs.Configuration;

/// <summary>
/// Настройки сервиса тарифов основного сайта (school-pi.online).
///
/// Сервис не держит паролей Робокассы вовсе — этим занимается сервер
/// ключей (offline/server/SchoolPiBoard.LicenseServer), тот же самый,
/// что уже продаёт лицензии на офлайн-доску и подписки на онлайн-доску.
/// У тарифов свой магазин Робокассы (третий по счёту), но пароли от него
/// лежат только на сервере ключей — здесь только общий секрет, которым
/// подписываются запросы между двумя своими службами.
///
/// Основной сайт (Flask, my_portfolio_project) про сервер ключей тоже
/// не знает — он обращается только сюда, как раньше.
/// </summary>
public sealed record TariffOptions
{
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Общий секрет между этим сервисом и основным сайтом (Flask).
    /// Проверяется заголовком X-Api-Key на всём, кроме /health, /plans
    /// и /callback — тот защищён общим секретом с сервером ключей, а не
    /// этим ключом.
    /// </summary>
    public required string ApiKey { get; init; }

    public required LicenseServerOptions LicenseServer { get; init; }

    public static TariffOptions Load(IConfiguration configuration)
    {
        var options = new TariffOptions
        {
            ConnectionString = configuration.GetConnectionString("Postgres")
                               ?? Env("DATABASE_CONNECTION_STRING")
                               ?? string.Empty,

            ApiKey = First(configuration["ApiKey"], "TARIFFS_API_KEY"),

            LicenseServer = new LicenseServerOptions
            {
                Url = (configuration["LicenseServer:Url"] ?? string.Empty).TrimEnd('/'),
                SharedSecret = First(configuration["LicenseServer:SharedSecret"], "TARIFFS_SHARED_SECRET"),
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
}

/// <summary>
/// Связь с сервером ключей — тот же приём, что у онлайн-доски
/// (schoolpiboard_online/server/.../KeyServerClient.cs). Общий секрет
/// должен совпадать с тем, что на сервере ключей записан в
/// Tariffs:SharedSecret / TARIFFS_SHARED_SECRET.
/// </summary>
public sealed record LicenseServerOptions
{
    public required string Url { get; init; }
    public required string SharedSecret { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(SharedSecret);
}

/// <summary>
/// Тарифы ИИ-функций основного сайта. Цена и ключ тарифа — источник
/// истины здесь (это деньги), лимит токенов — источник истины в
/// my_app/billing.py на сайте. Ключи тарифов должны совпадать в обоих
/// местах, иначе сайт не поймёт статус оплаченной подписки.
/// </summary>
public sealed record TariffPlan(string Key, string Title, decimal Price);

public static class TariffPlans
{
    /// <summary>Единственный срок, который продаётся, — календарный месяц.</summary>
    public const int PeriodDays = 30;

    public static readonly IReadOnlyList<TariffPlan> All = new[]
    {
        new TariffPlan("tutor_basic", "Базовый", 290m),
        new TariffPlan("tutor_standard", "Стандарт", 590m),
        new TariffPlan("tutor_pro", "Профи", 990m),
        new TariffPlan("student_plus", "Ученик+", 150m),
    };

    public static TariffPlan? Find(string key) => All.FirstOrDefault(plan => plan.Key == key);
}
