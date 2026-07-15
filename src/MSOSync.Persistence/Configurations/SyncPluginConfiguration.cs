using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncPluginConfiguration : IEntityTypeConfiguration<SyncPlugin>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncPlugin> builder)
    {
        builder.ToTable("sync_plugin", Schema);
        builder.HasKey(e => e.PluginId);

        builder.Property(e => e.PluginId)
            .HasColumnName("plugin_id")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200);

        builder.Property(e => e.PluginName)
            .HasColumnName("plugin_name")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200);

        builder.Property(e => e.PluginVersion)
            .HasColumnName("plugin_version")
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasColumnType("nvarchar(20)")
            .HasMaxLength(20);

        builder.Property(e => e.Enabled)
            .HasColumnName("enabled")
            .HasColumnType("bit")
            .HasDefaultValue(true);

        builder.Property(e => e.InstalledAt)
            .HasColumnName("installed_at")
            .HasColumnType("datetime2");

        builder.Property(e => e.LastSeenAt)
            .HasColumnName("last_seen_at")
            .HasColumnType("datetime2");

        builder.Property(e => e.LastError)
            .HasColumnName("last_error")
            .HasColumnType("nvarchar(2000)")
            .HasMaxLength(2000);

        builder.Property(e => e.ManifestHash)
            .HasColumnName("manifest_hash")
            .HasColumnType("nvarchar(64)")
            .HasMaxLength(64);

        builder.Property(e => e.HostVersion)
            .HasColumnName("host_version")
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50);
    }
}
