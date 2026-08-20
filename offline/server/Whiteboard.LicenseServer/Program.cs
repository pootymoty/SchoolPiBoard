using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Whiteboard.LicenseServer.Configuration;
using Whiteboard.LicenseServer.Data;
using Whiteboard.LicenseServer.Endpoints;
using Whiteboard.LicenseServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Настройки читаются один раз и сразу проверяются: без обязательных
// секретов сервис не поднимается.
var options = ServerOptions.Load(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(options.License);
builder.Services.AddSingleton(options.Stripe);
builder.Services.AddSingleton(options.Smtp);
builder.Services.AddSingleton(options.Robokassa);
builder.Services.AddSingleton(options.Trial);
builder.Services.AddSingleton(options.Web);

// Повторные попытки Npgsql намеренно не включены: сервис сам открывает
// транзакции при активации, а стратегия повторов с ними несовместима.
builder.Services.AddDbContext<LicenseDbContext>(db => db.UseNpgsql(options.ConnectionString));

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<RobokassaService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<TrialService>();
builder.Services.AddScoped<PurchaseService>();

if (options.Smtp.IsConfigured)
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();

// Страница покупки живёт на сайте, а не здесь: обычная форма уходит сюда
// без CORS, но для запросов из скриптов домены нужно перечислить явно.
builder.Services.AddCors(cors =>
{
    cors.AddPolicy(PurchaseEndpoints.CorsPolicy, policy => policy
        .WithOrigins(options.Web.AllowedOrigins)
        .AllowAnyHeader()
        .WithMethods("POST"));
});

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Ограничение частоты — чтобы ключи нельзя было подбирать перебором.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.AddPolicy(LicenseEndpoints.ActivatePolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Проверок больше: их шлёт каждый установленный клиент раз в сутки,
    // и за одним адресом может стоять целый офис.
    limiter.AddPolicy(LicenseEndpoints.ValidatePolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

await DatabaseInitializer.ApplySchemaAsync(app.Services);

app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();

app.MapLicenseEndpoints();
app.MapTrialEndpoints();
app.MapPurchaseEndpoints();
app.MapStripeWebhook();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (!options.Smtp.IsConfigured)
    app.Logger.LogWarning("SMTP не настроен: письма с ключами пишутся в лог вместо отправки.");

if (!options.Stripe.IsConfigured)
    app.Logger.LogInformation("Вебхук Stripe выключен: секрет не задан. Продажи идут через Робокассу.");

if (!options.Robokassa.IsConfigured)
    app.Logger.LogWarning("Робокасса не настроена: покупка недоступна, /purchase/start вернёт 503.");
else if (options.Robokassa.IsTest)
    app.Logger.LogWarning("Робокасса работает в ТЕСТОВОМ режиме: деньги не списываются.");

app.Run();

// Ключ группировки для ограничителя. Адрес берётся из соединения: заголовку
// X-Forwarded-For доверяет только UseForwardedHeaders и только от известных
// прокси, иначе лимит обходился бы подделкой заголовка.
static string ClientKey(HttpContext context)
    => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
