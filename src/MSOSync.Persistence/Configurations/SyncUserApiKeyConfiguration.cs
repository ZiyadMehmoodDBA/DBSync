using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserApiKeyConfiguration : IEntityTypeConfiguration<SyncUserApiKey>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUserApiKey> builder)
    {
        builder.ToTable("sync_user_api_key", Schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(8).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasColumnType("datetime2(7)");
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(7)");
        builder.Property(e => e.IsRevoked).HasColumnName("is_revoked").HasDefaultValue(false);
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime2(7)");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("FK_sync_user_api_key_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_sync_user_api_key_user_id");
        builder.HasIndex(e => e.KeyHash).IsUnique().HasDatabaseName("IX_sync_user_api_key_key_hash");
    }
}
