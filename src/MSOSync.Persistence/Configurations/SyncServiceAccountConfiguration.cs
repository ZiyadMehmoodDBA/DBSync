using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncServiceAccountConfiguration : IEntityTypeConfiguration<SyncServiceAccount>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncServiceAccount> builder)
    {
        builder.ToTable("sync_service_account", Schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        // Column name stays "description" to avoid a migration; C# property renamed to PermissionsJson
        // to make it clear this field stores JSON-serialized string[] of permissions, not a human label.
        builder.Property(e => e.PermissionsJson).HasColumnName("description").HasMaxLength(1024);
        builder.Property(e => e.ClientId).HasColumnName("client_id").HasMaxLength(128).IsRequired();
        builder.Property(e => e.ClientSecretHash).HasColumnName("client_secret_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasColumnType("datetime2(7)");
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime2(7)");
        builder.Property(e => e.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        builder.Property(e => e.IsRevoked).HasColumnName("is_revoked").HasDefaultValue(false);
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("datetime2(7)");
        builder.Property(e => e.RevokedReason).HasColumnName("revoked_reason").HasMaxLength(512);

        builder.HasIndex(e => e.ClientId).IsUnique().HasDatabaseName("IX_sync_service_account_client_id");
        builder.HasIndex(e => e.IsEnabled).HasDatabaseName("IX_sync_service_account_is_enabled");
    }
}
