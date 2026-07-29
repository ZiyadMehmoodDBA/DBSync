using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserTotpSecretConfiguration : IEntityTypeConfiguration<SyncUserTotpSecret>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUserTotpSecret> builder)
    {
        builder.ToTable("sync_user_totp_secret", Schema);
        builder.HasKey(e => e.UserId);

        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.Secret).HasColumnName("secret").HasMaxLength(64).IsRequired();
        builder.Property(e => e.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(false);
        builder.Property(e => e.EnabledAt).HasColumnName("enabled_at").HasColumnType("datetime2(7)");

        builder.HasOne(e => e.User)
            .WithOne()
            .HasForeignKey<SyncUserTotpSecret>(e => e.UserId)
            .HasConstraintName("FK_sync_user_totp_secret_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
