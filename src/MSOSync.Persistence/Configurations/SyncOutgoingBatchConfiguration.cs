using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncOutgoingBatchConfiguration : IEntityTypeConfiguration<SyncOutgoingBatch>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncOutgoingBatch> builder)
    {
        builder.ToTable("sync_outgoing_batch", Schema);
        builder.HasKey(e => e.BatchId);

        builder.Property(e => e.BatchId).HasColumnName("batch_id").ValueGeneratedOnAdd();
        builder.Property(e => e.BatchSequence).HasColumnName("batch_sequence").IsRequired();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("tinyint").HasConversion<byte>();
        builder.Property(e => e.RowCount).HasColumnName("row_count").HasDefaultValue(0);
        builder.Property(e => e.ByteCount).HasColumnName("byte_count").HasDefaultValue(0L);
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        builder.Property(e => e.NextRetryTime).HasColumnName("next_retry_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.NetworkMillis).HasColumnName("network_millis");
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.SentTime).HasColumnName("sent_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.AckTime).HasColumnName("ack_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => new { e.NodeId, e.Status }).HasDatabaseName("IX_sync_outgoing_batch_node_status");
        builder.HasIndex(e => e.NextRetryTime).HasDatabaseName("IX_sync_outgoing_batch_next_retry");
        builder.HasIndex(e => e.ChannelId).HasDatabaseName("IX_sync_outgoing_batch_channel");
    }
}
