using Microsoft.EntityFrameworkCore;

namespace Whiteboard.LicenseServer.Data;

/// <summary>
/// Применяет схему при старте. Для сервиса из двух таблиц это проще
/// и прозрачнее миграций EF: один идемпотентный SQL-скрипт, который
/// при желании можно выполнить руками.
/// </summary>
public static class DatabaseInitializer
{
    private const string ScriptRelativePath = "sql/001_init.sql";

    public static async Task ApplySchemaAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));

        var path = Path.Combine(AppContext.BaseDirectory, ScriptRelativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Не найден скрипт схемы: {path}");

        var sql = await File.ReadAllTextAsync(path, cancellationToken);

        var database = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();
        await database.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        logger.LogInformation("Схема базы данных актуальна.");
    }
}
