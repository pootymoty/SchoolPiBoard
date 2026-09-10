using Microsoft.EntityFrameworkCore;

namespace SchoolPi.Tariffs.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Subscription>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.ExternalUserId).HasColumnName("external_user_id");
            entity.Property(x => x.Plan).HasColumnName("plan").IsRequired();
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => x.ExternalUserId).IsUnique();
        });

        model.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.ExternalUserId).HasColumnName("external_user_id");
            entity.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            entity.Property(x => x.Plan).HasColumnName("plan").IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
            entity.Property(x => x.Status).HasColumnName("status").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.PaidAt).HasColumnName("paid_at");
            entity.HasIndex(x => x.InvoiceId).IsUnique();
        });
    }
}

/// <summary>Применяет схему при старте: все скрипты из папки sql по порядку имён.</summary>
public static class DatabaseInitializer
{
    public static async Task ApplySchemaAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));

        var folder = Path.Combine(AppContext.BaseDirectory, "sql");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Не найдена папка со схемой: {folder}");

        var scripts = Directory.GetFiles(folder, "*.sql");
        Array.Sort(scripts, StringComparer.Ordinal);

        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var script in scripts)
        {
            var sql = await File.ReadAllTextAsync(script, cancellationToken);
            await database.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.LogInformation("Применён скрипт схемы {Script}.", Path.GetFileName(script));
        }
    }
}
