using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SchoolPi.Tariffs.Configuration;
using SchoolPi.Tariffs.Data;
using SchoolPi.Tariffs.Endpoints;
using SchoolPi.Tariffs.Services;

var builder = WebApplication.CreateBuilder(args);

var options = TariffOptions.Load(builder.Configuration);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(options.LicenseServer);

// Повторные попытки Npgsql намеренно не включены — как и в других сервисах,
// часть операций идёт в своих транзакциях.
builder.Services.AddDbContext<AppDbContext>(db => db.UseNpgsql(options.ConnectionString));

builder.Services.AddSingleton<LicenseServerClient>();
builder.Services.AddScoped<SubscriptionService>();

// Запрос к серверу ключей и списания идут обычным HTTP-клиентом.
builder.Services.AddHttpClient();
builder.Services.AddHostedService<AutoRenewService>();

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

await DatabaseInitializer.ApplySchemaAsync(app.Services);

app.UseForwardedHeaders();

app.MapTariffEndpoints(options);

if (!options.LicenseServer.IsConfigured)
    app.Logger.LogWarning("Связь с сервером ключей не настроена: /checkout вернёт 503.");

app.Run();
