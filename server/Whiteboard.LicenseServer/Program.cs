using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Whiteboard.LicenseServer.Configuration;
using Whiteboard.LicenseServer.Data;
using Whiteboard.LicenseServer.Endpoints;
using Whiteboard.LicenseServer.Realtime;
using Whiteboard.LicenseServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Настройки читаются один раз и сразу проверяются: без обязательных
// секретов сервис не поднимается.
var options = ServerOptions.Load(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(options.License);
builder.Services.AddSingleton(options.Stripe);
builder.Services.AddSingleton(options.Email);
builder.Services.AddSingleton(options.Robokassa);
builder.Services.AddSingleton(options.Trial);
builder.Services.AddSingleton(options.Web);
builder.Services.AddSingleton(options.Auth);
builder.Services.AddSingleton(options.Redis);

// Повторные попытки Npgsql намеренно не включены: сервис сам открывает
// транзакции при активации, а стратегия повторов с ними несовместима.
builder.Services.AddDbContext<LicenseDbContext>(db => db.UseNpgsql(options.ConnectionString));

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<RobokassaService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<TrialService>();
builder.Services.AddScoped<PurchaseService>();

// ---------- веб-версия ----------

builder.Services.AddSingleton<AuthTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BoardService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        // Претензии не переименовываются: в коде читается ровно «sub».
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = AuthTokenService.CreateValidationParameters(options.Auth);

        jwt.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // WebSocket не умеет отправлять заголовок Authorization,
                // поэтому SignalR передаёт токен строкой запроса. Принимаем
                // его только для адреса хаба, не для всего API.
                var token = context.Request.Query["access_token"].ToString();

                if (!string.IsNullOrEmpty(token) &&
                    context.HttpContext.Request.Path.StartsWithSegments(BoardHub.Path))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var realtime = builder.Services.AddSignalR();

if (options.Redis.IsConfigured)
{
    // Подключаемся сразу: без Redis в бою сервер всё равно работать не должен,
    // и узнать об этом лучше при старте, чем на первом участнике доски.
    var redis = ConnectionMultiplexer.Connect(options.Redis.ConnectionString);

    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddSingleton<IPresenceStore, RedisPresenceStore>();
    realtime.AddStackExchangeRedis(options.Redis.ConnectionString);
}
else
{
    builder.Services.AddSingleton<IPresenceStore, MemoryPresenceStore>();
}

if (options.Email.IsConfigured)
    builder.Services.AddHttpClient<IEmailSender, SendGridEmailSender>();
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

    // Веб-приложению нужно больше: свои заголовки, все методы и WebSocket.
    cors.AddPolicy(AuthEndpoints.CorsPolicy, policy => policy
        .WithOrigins(options.Web.AppOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
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
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapLicenseEndpoints();
app.MapTrialEndpoints();
app.MapPurchaseEndpoints();
app.MapStripeWebhook();

app.MapAuthEndpoints();
app.MapBoardEndpoints();
app.MapBillingEndpoints();
app.MapHub<BoardHub>(BoardHub.Path).RequireCors(AuthEndpoints.CorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (!options.Redis.IsConfigured)
{
    app.Logger.LogWarning(
        "Redis не настроен: присутствие участников хранится в памяти процесса. " +
        "Для нескольких инстансов сервера это работать не будет.");
}

if (options.Web.AppOrigins.Length == 0)
    app.Logger.LogWarning("Web:AppOrigins пуст: браузер не пустит веб-приложение к API.");

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
