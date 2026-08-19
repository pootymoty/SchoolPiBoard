using Microsoft.EntityFrameworkCore;

namespace Whiteboard.LicenseServer.Data;

public class LicenseDbContext : DbContext
{
    public LicenseDbContext(DbContextOptions<LicenseDbContext> options) : base(options)
    {
    }

    public DbSet<License> Licenses => Set<License>();

    public DbSet<LicenseActivation> Activations => Set<LicenseActivation>();

    public DbSet<TrialActivation> Trials => Set<TrialActivation>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();

    public DbSet<BoardItem> BoardItems => Set<BoardItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<License>(entity =>
        {
            entity.ToTable("licenses");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Key).HasColumnName("key").IsRequired();
            entity.Property(x => x.Email).HasColumnName("email").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.Revoked).HasColumnName("revoked");
            entity.Property(x => x.StripePaymentHash).HasColumnName("stripe_payment_hash");
            entity.Property(x => x.EmailSentAt).HasColumnName("email_sent_at");

            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => x.StripePaymentHash).IsUnique();

            entity.HasMany(x => x.Activations)
                  .WithOne(x => x.License!)
                  .HasForeignKey(x => x.LicenseId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LicenseActivation>(entity =>
        {
            entity.ToTable("license_activations");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.LicenseId).HasColumnName("license_id");
            entity.Property(x => x.HardwareId).HasColumnName("hardware_id").IsRequired();
            entity.Property(x => x.ActivatedAt).HasColumnName("activated_at");
            entity.Property(x => x.LastValidatedAt).HasColumnName("last_validated_at");

            entity.HasIndex(x => new { x.LicenseId, x.HardwareId }).IsUnique();
        });

        modelBuilder.Entity<TrialActivation>(entity =>
        {
            entity.ToTable("trial_activations");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.HardwareId).HasColumnName("hardware_id").IsRequired();
            entity.Property(x => x.Email).HasColumnName("email").IsRequired();
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");

            entity.HasIndex(x => x.HardwareId).IsUnique();
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.InvoiceId).HasColumnName("invoice_id");
            entity.Property(x => x.Email).HasColumnName("email").IsRequired();
            entity.Property(x => x.Amount).HasColumnName("amount").HasPrecision(12, 2);
            entity.Property(x => x.Provider).HasColumnName("provider").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.PaidAt).HasColumnName("paid_at");
            entity.Property(x => x.LicenseId).HasColumnName("license_id");

            entity.HasIndex(x => x.InvoiceId).IsUnique();
        });

        // ---------- веб-версия ----------

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Email).HasColumnName("email").IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");

            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Provider).HasColumnName("provider").IsRequired();
            entity.Property(x => x.ExternalId).HasColumnName("external_id");
            entity.Property(x => x.Plan).HasColumnName("plan").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").IsRequired();
            entity.Property(x => x.TrialEndsAt).HasColumnName("trial_ends_at");
            entity.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<Board>(entity =>
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

            entity.HasMany(x => x.Members)
                  .WithOne()
                  .HasForeignKey(x => x.BoardId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<BoardMember>(entity =>
        {
            entity.ToTable("board_members");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.BoardId).HasColumnName("board_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Role).HasColumnName("role").IsRequired();
            entity.Property(x => x.InvitedAt).HasColumnName("invited_at");

            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.BoardId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<BoardItem>(entity =>
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
