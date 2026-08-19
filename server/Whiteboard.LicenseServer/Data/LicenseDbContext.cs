using Microsoft.EntityFrameworkCore;

namespace Whiteboard.LicenseServer.Data;

public class LicenseDbContext : DbContext
{
    public LicenseDbContext(DbContextOptions<LicenseDbContext> options) : base(options)
    {
    }

    public DbSet<License> Licenses => Set<License>();

    public DbSet<LicenseActivation> Activations => Set<LicenseActivation>();

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
    }
}
