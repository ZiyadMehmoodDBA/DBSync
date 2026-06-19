using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncIncomingBatchConfiguration : IEntityTypeConfiguration<SyncIncomingBatch>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncIncomingBatch> builder)
    {
        builder.ToTable("sync_incoming_batch", Schema);
        builder.HasKey(e => e.BatchId);

        builder.Property(e => e.BatchId).HasColumnName("batch_id").ValueGeneratedNever();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("tinyint").HasConversion<byte>();
        builder.Property(e => e.RowCount).HasColumnName("row_count");
        builder.Property(e => e.LoadTime).HasColumnName("load_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ExtractTime).HasColumnName("extract_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.AppliedTime).HasColumnName("applied_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ApplyTimeMs).HasColumnName("apply_time_ms");

        builder.HasIndex(e => new { e.NodeId, e.Status }).HasDatabaseName("IX_sync_incoming_batch_node_status");
    }
}
