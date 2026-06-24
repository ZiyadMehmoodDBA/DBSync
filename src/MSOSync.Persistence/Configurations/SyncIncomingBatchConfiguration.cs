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
        builder.Property(e => e.BatchSequence).HasColumnName("batch_sequence").IsRequired();
        builder.Property(e => e.SourceNodeId).HasColumnName("source_node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ReceivedTime).HasColumnName("received_time").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.LoadTime).HasColumnName("load_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ExtractTime).HasColumnName("extract_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.AppliedTime).HasColumnName("applied_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ApplyTimeMs).HasColumnName("apply_time_ms");

        builder.HasIndex(e => new { e.NodeId, e.Status }).HasDatabaseName("IX_sync_incoming_batch_node_status");
        builder.HasIndex(e => new { e.SourceNodeId, e.ChannelId, e.BatchSequence })
            .HasDatabaseName("IX_sync_incoming_batch_source_channel_sequence");
        builder.HasIndex(e => e.ReceivedTime)
            .IsDescending(true)
            .HasDatabaseName("IX_sync_incoming_batch_received_time");
        builder.HasIndex(e => new { e.SourceNodeId, e.ReceivedTime })
            .IsDescending(false, true)
            .HasDatabaseName("IX_sync_incoming_batch_source_node_time");
        builder.HasIndex(e => new { e.Status, e.ReceivedTime })
            .IsDescending(false, true)
            .HasDatabaseName("IX_sync_incoming_batch_status_time");

        builder.HasOne<SyncNode>()
            .WithMany()
            .HasForeignKey(e => e.SourceNodeId)
            .HasConstraintName("FK_sync_incoming_batch_source_node")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
