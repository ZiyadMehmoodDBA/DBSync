using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRouterConfiguration : IEntityTypeConfiguration<SyncRouter>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRouter> builder)
    {
        builder.ToTable("sync_router", Schema);
        builder.HasKey(e => e.RouterId);

        builder.Property(e => e.RouterId).HasColumnName("router_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SourceNodeGroup).HasColumnName("source_node_group").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.TargetNodeGroup).HasColumnName("target_node_group").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.RouterType).HasColumnName("router_type").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).HasDefaultValue("default");
        builder.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
    }
}
