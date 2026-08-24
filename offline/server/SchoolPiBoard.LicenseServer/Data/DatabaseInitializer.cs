using Microsoft.EntityFrameworkCore;

namespace SchoolPiBoard.LicenseServer.Data;

/// <summary>
/// Применяет схему при старте: все скрипты из папки sql по порядку имён.
/// Скрипты идемпотентные, поэтому повторный запуск ничего не ломает — для
/// сервиса такого размера это проще и прозрачнее миграций EF.
/// </summary>
public static class DatabaseInitializer
{
    private const string ScriptFolder = "sql";

    public static async Task ApplySchemaAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));

        var folder = Path.Combine(AppContext.BaseDirectory, ScriptFolder);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Не найдена папка со схемой: {folder}");

        var scripts = Directory.GetFiles(folder, "*.sql");
        Array.Sort(scripts, StringComparer.Ordinal);

        if (scripts.Length == 0)
            throw new FileNotFoundException($"В папке {folder} нет ни одного скрипта схемы.");

        var database = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();

        foreach (var script in scripts)
        {
            var sql = await File.ReadAllTextAsync(script, cancellationToken);
            await database.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.LogInformation("Применён скрипт схемы {Script}.", Path.GetFileName(script));
        }
    }
}
