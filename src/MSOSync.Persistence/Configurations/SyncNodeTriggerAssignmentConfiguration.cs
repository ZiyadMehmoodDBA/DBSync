using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeTriggerAssignmentConfiguration : IEntityTypeConfiguration<SyncNodeTriggerAssignment>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeTriggerAssignment> builder)
    {
        builder.ToTable("sync_node_trigger", Schema);
        builder.HasKey(e => new { e.NodeId, e.TriggerId });

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.TriggerId).HasColumnName("trigger_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
    }
}
