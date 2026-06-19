using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRuntimeStatsConfiguration : IEntityTypeConfiguration<SyncRuntimeStats>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRuntimeStats> builder)
    {
        builder.ToTable("sync_runtime_stats", Schema);
        builder.HasKey(e => e.StatId);

        builder.Property(e => e.StatId).HasColumnName("stat_id").ValueGeneratedOnAdd();
        builder.Property(e => e.HeapUsed).HasColumnName("heap_used");
        builder.Property(e => e.HeapMax).HasColumnName("heap_max");
        builder.Property(e => e.ThreadCount).HasColumnName("thread_count");
        builder.Property(e => e.CpuPercent).HasColumnName("cpu_percent").HasColumnType("decimal(5,2)");
        builder.Property(e => e.GcCount).HasColumnName("gc_count");
        builder.Property(e => e.GcTimeMs).HasColumnName("gc_time_ms");
        builder.Property(e => e.UptimeMs).HasColumnName("uptime_ms");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");
    }
}
