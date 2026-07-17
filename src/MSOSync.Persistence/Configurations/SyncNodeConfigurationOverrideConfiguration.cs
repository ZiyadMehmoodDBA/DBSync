using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeConfigurationOverrideConfiguration
    : IEntityTypeConfiguration<SyncNodeConfigurationOverride>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeConfigurationOverride> builder)
    {
        builder.ToTable("sync_node_configuration_override", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("NEWID()");
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.SettingKey).HasColumnName("setting_key")
            .HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.SettingValue).HasColumnName("setting_value")
            .HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(e => e.OverrideSource).HasColumnName("override_source")
            .HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => new { e.NodeId, e.SettingKey }).IsUnique()
            .HasDatabaseName("UX_node_override_key");

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.NodeId })
            .HasDatabaseName("IX_sync_node_configuration_override_TenantId_NodeId");
    }
}
