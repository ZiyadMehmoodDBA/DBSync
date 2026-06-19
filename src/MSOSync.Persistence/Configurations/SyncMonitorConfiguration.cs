using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncMonitorConfiguration : IEntityTypeConfiguration<SyncMonitor>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncMonitor> builder)
    {
        builder.ToTable("sync_monitor", Schema);
        builder.HasKey(e => e.SnapshotId);

        builder.Property(e => e.SnapshotId).HasColumnName("snapshot_id").ValueGeneratedOnAdd();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.MetricName).HasColumnName("metric_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.MetricValue).HasColumnName("metric_value").HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => new { e.NodeId, e.CreateTime }).HasDatabaseName("IX_sync_monitor_node_create_time");
    }
}
