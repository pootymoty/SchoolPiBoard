using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.Online.Configuration;
using SchoolPiBoard.Online.Data;
using SchoolPiBoard.Online.Endpoints;
using SchoolPiBoard.Online.Realtime;
using SchoolPiBoard.Online.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Настройки читаются один раз и сразу проверяются: без обязательных
// секретов сервис не поднимается.
var options = OnlineOptions.Load(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(options.Auth);
builder.Services.AddSingleton(options.Site);
builder.Services.AddSingleton(options.Smtp);
builder.Services.AddSingleton(options.Captcha);
builder.Services.AddSingleton(options.Payments);
builder.Services.AddSingleton(options.Invites);

// Повторные попытки Npgsql намеренно не включены: часть операций идёт
// в своих транзакциях, а стратегия повторов с ними несовместима.
builder.Services.AddDbContext<AppDbContext>(db => db.UseNpgsql(options.ConnectionString));

builder.Services.AddSingleton<AuthTokenService>();
builder.Services.AddSingleton<RobokassaService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<BoardService>();

if (options.Smtp.IsConfigured)
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();

if (options.Captcha.Provider == "yandex")
    builder.Services.AddHttpClient<ICaptchaVerifier, YandexSmartCaptchaVerifier>();
else
    builder.Services.AddSingleton<ICaptchaVerifier, DisabledCaptchaVerifier>();

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
                // WebSocket не умеет слать заголовок Authorization, поэтому
                // SignalR передаёт токен строкой запроса. Принимаем его
                // только для адреса хаба, не для всего API.
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
    // Подключаемся при старте: без Redis в бою сервис работать не должен,
    // и узнать об этом лучше сразу, а не на первом участнике доски.
    var redis = ConnectionMultiplexer.Connect(options.Redis.ConnectionString);

    builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
    builder.Services.AddSingleton<IPresenceStore, RedisPresenceStore>();
    realtime.AddStackExchangeRedis(options.Redis.ConnectionString);
}
else
{
    builder.Services.AddSingleton<IPresenceStore, MemoryPresenceStore>();
}

builder.Services.AddCors(cors =>
{
    cors.AddPolicy(AuthEndpoints.CorsPolicy, policy => policy
        .WithOrigins(options.Site.AppOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Ограничение частоты на вход, регистрацию и оплату — защита от перебора.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    limiter.AddPolicy(AuthEndpoints.AuthRateLimit, http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
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

app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapBillingEndpoints();
app.MapBoardEndpoints();
app.MapHub<BoardHub>(BoardHub.Path).RequireCors(AuthEndpoints.CorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (!options.Redis.IsConfigured)
{
    app.Logger.LogWarning(
        "Redis не настроен: присутствие участников живёт в памяти процесса. " +
        "Для нескольких инстансов это работать не будет.");
}

if (!options.Smtp.IsConfigured)
    app.Logger.LogWarning("SMTP не настроен: письма пишутся в лог вместо отправки.");

if (options.Captcha.Provider != "yandex")
{
    app.Logger.LogWarning(
        "Капча выключена: регистрация ничем не защищена от роботов. " +
        "Включается одним значением Captcha:Provider=yandex, когда появится ключ SmartCaptcha.");
}

if (!options.Payments.IsConfigured)
    app.Logger.LogWarning("Робокасса не настроена: оплата подписки вернёт 503.");

if (options.Site.AppOrigins.Length == 0)
    app.Logger.LogWarning("Site:AppOrigins пуст: браузер не пустит веб-приложение к API.");

app.Run();

// Ключ группировки для ограничителя. Адрес берётся из соединения: заголовку
// X-Forwarded-For доверяет только UseForwardedHeaders и только от известных
// прокси, иначе лимит обходился бы подделкой заголовка.
static string ClientKey(HttpContext context)
    => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
