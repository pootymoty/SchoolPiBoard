using Microsoft.EntityFrameworkCore;

namespace SchoolPiBoard.Online.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<EmailAction> EmailActions => Set<EmailAction>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<BoardInvite> BoardInvites => Set<BoardInvite>();
    public DbSet<BoardItem> BoardItems => Set<BoardItem>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Email).HasColumnName("email").IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(x => x.LastName).HasColumnName("last_name").IsRequired();
            entity.Property(x => x.FirstName).HasColumnName("first_name").IsRequired();
            entity.Property(x => x.BirthDate).HasColumnName("birth_date");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.TrialUsedAt).HasColumnName("trial_used_at");
            entity.HasIndex(x => x.Email).IsUnique();
        });

        model.Entity<PendingRegistration>(entity =>
        {
            entity.ToTable("pending_registrations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Email).HasColumnName("email").IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(x => x.LastName).HasColumnName("last_name").IsRequired();
            entity.Property(x => x.FirstName).HasColumnName("first_name").IsRequired();
            entity.Property(x => x.BirthDate).HasColumnName("birth_date");
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.TokenHash);
        });

        model.Entity<EmailAction>(entity =>
        {
            entity.ToTable("email_actions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Kind).HasColumnName("kind").IsRequired();
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.UsedAt).HasColumnName("used_at");
            entity.HasIndex(x => x.TokenHash);
        });

        model.Entity<Subscription>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Kind).HasColumnName("kind").IsRequired();
            entity.Property(x => x.PlanDays).HasColumnName("plan_days");
            entity.Property(x => x.Status).HasColumnName("status").IsRequired();
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.AutoRenew).HasColumnName("auto_renew");
            entity.Property(x => x.Provider).HasColumnName("provider");
            entity.Property(x => x.ExternalId).HasColumnName("external_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => x.UserId).IsUnique();
        });

        model.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            entity.Property(x => x.PlanDays).HasColumnName("plan_days");
            entity.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
            entity.Property(x => x.Provider).HasColumnName("provider").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.PaidAt).HasColumnName("paid_at");
            entity.HasIndex(x => x.InvoiceId).IsUnique();
        });

        model.Entity<Board>(entity =>
        {
            entity.ToTable("boards");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerId).HasColumnName("owner_id");
            entity.Property(x => x.Name).HasColumnName("name").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ModifiedAt).HasColumnName("modified_at");
            entity.Property(x => x.Archived).HasColumnName("archived");
            entity.Property(x => x.BackgroundStyle).HasColumnName("background_style").IsRequired();
            entity.Property(x => x.BackgroundColor).HasColumnName("background_color").IsRequired();
            entity.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.OwnerId);
        });

        model.Entity<BoardMember>(entity =>
        {
            entity.ToTable("board_members");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BoardId).HasColumnName("board_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Role).HasColumnName("role").IsRequired();
            entity.Property(x => x.InvitedAt).HasColumnName("invited_at");
            entity.Property(x => x.ViaLink).HasColumnName("via_link");
            entity.Property(x => x.EditUntil).HasColumnName("edit_until");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.BoardId, x.UserId }).IsUnique();
        });

        model.Entity<BoardInvite>(entity =>
        {
            entity.ToTable("board_invites");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BoardId).HasColumnName("board_id");
            entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.Property(x => x.Role).HasColumnName("role").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            entity.Property(x => x.Uses).HasColumnName("uses");
            entity.Property(x => x.EditDays).HasColumnName("edit_days");
            entity.HasIndex(x => x.TokenHash);
        });

        model.Entity<BoardItem>(entity =>
        {
            entity.ToTable("board_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BoardId).HasColumnName("board_id");
            entity.Property(x => x.Kind).HasColumnName("kind").IsRequired();
            entity.Property(x => x.X).HasColumnName("x");
            entity.Property(x => x.Y).HasColumnName("y");
            entity.Property(x => x.W).HasColumnName("w");
            entity.Property(x => x.H).HasColumnName("h");
            entity.Property(x => x.Rotation).HasColumnName("rotation");
            entity.Property(x => x.ZIndex).HasColumnName("z_index");
            entity.Property(x => x.StrokeColor).HasColumnName("stroke_color");
            entity.Property(x => x.FillColor).HasColumnName("fill_color");
            entity.Property(x => x.Thickness).HasColumnName("thickness");
            entity.Property(x => x.Opacity).HasColumnName("opacity");
            entity.Property(x => x.Points).HasColumnName("points").HasColumnType("jsonb");
            entity.Property(x => x.Text).HasColumnName("text");
            entity.Property(x => x.FontSize).HasColumnName("font_size");
            entity.Property(x => x.ImageRef).HasColumnName("image_ref");
            entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => x.BoardId);
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
