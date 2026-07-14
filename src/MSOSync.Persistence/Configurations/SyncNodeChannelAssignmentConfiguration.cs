using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeChannelAssignmentConfiguration : IEntityTypeConfiguration<SyncNodeChannelAssignment>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeChannelAssignment> builder)
    {
        builder.ToTable("sync_node_channel", Schema);
        builder.HasKey(e => new { e.NodeId, e.ChannelId });

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
    }
}
