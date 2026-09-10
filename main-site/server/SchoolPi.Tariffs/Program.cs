using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SchoolPi.Tariffs.Configuration;
using SchoolPi.Tariffs.Data;
using SchoolPi.Tariffs.Endpoints;
using SchoolPi.Tariffs.Services;

var builder = WebApplication.CreateBuilder(args);

var options = TariffOptions.Load(builder.Configuration);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(options.Payments);

// Повторные попытки Npgsql намеренно не включены — как и в online/server,
// часть операций идёт в своих транзакциях.
builder.Services.AddDbContext<AppDbContext>(db => db.UseNpgsql(options.ConnectionString));

builder.Services.AddSingleton<RobokassaService>();
builder.Services.AddScoped<SubscriptionService>();

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

await DatabaseInitializer.ApplySchemaAsync(app.Services);

app.UseForwardedHeaders();

app.MapTariffEndpoints(options);

if (!options.Payments.IsConfigured)
    app.Logger.LogWarning("Робокасса не настроена: /checkout вернёт 503.");

app.Run();
