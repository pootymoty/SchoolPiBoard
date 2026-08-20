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
            entity.Property(x => x.PaymentIdHash).HasColumnName("payment_hash");
            entity.Property(x => x.EmailSentAt).HasColumnName("email_sent_at");

            entity.HasIndex(x => x.Key).IsUnique();
            entity.HasIndex(x => x.PaymentIdHash).IsUnique();

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
    }
}
