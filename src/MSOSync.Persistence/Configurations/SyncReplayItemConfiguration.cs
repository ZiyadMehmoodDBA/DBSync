using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncReplayItemConfiguration : IEntityTypeConfiguration<SyncReplayItem>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncReplayItem> b)
    {
        b.ToTable("sync_replay_item", Schema);
        b.HasKey(x => x.ItemId);
        b.Property(x => x.ItemId).HasColumnName("item_id").ValueGeneratedNever();
        b.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
        b.Property(x => x.SourceBatchId).HasColumnName("source_batch_id");
        b.Property(x => x.ReplayBatchId).HasColumnName("replay_batch_id");
        b.Property(x => x.NodeId).HasColumnName("node_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.ChannelId).HasColumnName("channel_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.EventCount).HasColumnName("event_count").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

        b.HasOne<SyncOperation>().WithMany().HasForeignKey(x => x.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.OperationId, x.Status })
            .HasDatabaseName("ix_sync_replay_item_op_status");
        b.HasIndex(x => new { x.TenantId, x.NodeId })
            .HasDatabaseName("ix_sync_replay_item_tenant_node");
    }
}
