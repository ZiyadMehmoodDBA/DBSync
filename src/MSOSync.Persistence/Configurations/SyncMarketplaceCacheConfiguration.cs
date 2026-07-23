using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncMarketplaceCacheConfiguration : IEntityTypeConfiguration<SyncMarketplaceCache>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncMarketplaceCache> builder)
    {
        builder.ToTable("sync_marketplace_cache", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .UseIdentityColumn();

        builder.Property(e => e.RegistryUrl)
               .HasColumnName("registry_url")
               .HasColumnType("nvarchar(500)")
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(e => e.PluginId)
               .HasColumnName("plugin_id")
               .HasColumnType("nvarchar(200)")
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(e => e.LatestVersion)
               .HasColumnName("latest_version")
               .HasColumnType("nvarchar(50)")
               .HasMaxLength(50)
               .IsRequired();

        builder.Property(e => e.MetadataJson)
               .HasColumnName("metadata_json")
               .HasColumnType("nvarchar(max)")
               .IsRequired();

        builder.Property(e => e.CachedAt)
               .HasColumnName("cached_at")
               .HasColumnType("datetime2")
               .IsRequired();

        builder.Property(e => e.ExpiresAt)
               .HasColumnName("expires_at")
               .HasColumnType("datetime2")
               .IsRequired();

        // One cache entry per (registry, pluginId)
        builder.HasIndex(e => new { e.RegistryUrl, e.PluginId })
               .IsUnique()
               .HasDatabaseName("IX_sync_marketplace_cache_registry_plugin");

        // Expiry-based sweep index
        builder.HasIndex(e => e.ExpiresAt)
               .HasDatabaseName("IX_sync_marketplace_cache_expires_at");
    }
}
