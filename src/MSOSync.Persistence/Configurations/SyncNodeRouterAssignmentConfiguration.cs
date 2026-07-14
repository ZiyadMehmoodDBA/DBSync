using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeRouterAssignmentConfiguration : IEntityTypeConfiguration<SyncNodeRouterAssignment>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeRouterAssignment> builder)
    {
        builder.ToTable("sync_node_router", Schema);
        builder.HasKey(e => new { e.NodeId, e.RouterId });

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.RouterId).HasColumnName("router_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
    }
}
