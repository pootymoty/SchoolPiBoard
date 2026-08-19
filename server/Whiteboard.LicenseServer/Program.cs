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
builder.Services.AddSingleton(options.Email);

// Повторные попытки Npgsql намеренно не включены: сервис сам открывает
// транзакции при активации, а стратегия повторов с ними несовместима.
builder.Services.AddDbContext<LicenseDbContext>(db => db.UseNpgsql(options.ConnectionString));

builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<LicenseService>();

if (options.Email.IsConfigured)
    builder.Services.AddHttpClient<IEmailSender, SendGridEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();

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
app.UseRateLimiter();

app.MapLicenseEndpoints();
app.MapStripeWebhook();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Ключ группировки для ограничителя. Адрес берётся из соединения: заголовку
// X-Forwarded-For доверяет только UseForwardedHeaders и только от известных
// прокси, иначе лимит обходился бы подделкой заголовка.
static string ClientKey(HttpContext context)
    => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
