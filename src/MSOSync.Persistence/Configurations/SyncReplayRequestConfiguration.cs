using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncReplayRequestConfiguration : IEntityTypeConfiguration<SyncReplayRequest>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncReplayRequest> b)
    {
        b.ToTable("sync_replay_request", Schema);
        b.HasKey(x => x.ReplayId);
        b.Property(x => x.ReplayId).HasColumnName("replay_id").ValueGeneratedNever();
        b.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
        b.Property(x => x.NodeId).HasColumnName("node_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.ChannelIdsJson).HasColumnName("channel_ids_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.BatchIdsJson).HasColumnName("batch_ids_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.FromTime).HasColumnName("from_time").HasColumnType("datetime2(7)").IsRequired();
        b.Property(x => x.ToTime).HasColumnName("to_time").HasColumnType("datetime2(7)").IsRequired();
        b.Property(x => x.ReplayMode).HasColumnName("replay_mode").HasMaxLength(20).IsRequired();
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

        b.HasOne<SyncOperation>().WithMany().HasForeignKey(x => x.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
