using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserConfiguration : IEntityTypeConfiguration<SyncUser>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUser> builder)
    {
        builder.ToTable("sync_user", Schema);
        builder.HasKey(e => e.UserId);

        builder.Property(e => e.UserId).HasColumnName("user_id").ValueGeneratedOnAdd();
        builder.Property(e => e.Username).HasColumnName("username").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.PasswordHash).HasColumnName("password_hash").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
        builder.Property(e => e.LastLogin).HasColumnName("last_login").HasColumnType("datetime2(7)");
        builder.Property(e => e.FailedAttempts).HasColumnName("failed_attempts").HasDefaultValue(0);
        builder.Property(e => e.CreatedTime).HasColumnName("created_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => e.Username).IsUnique().HasDatabaseName("UQ_sync_user_username");
    }
}
