using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeGroupConfiguration : IEntityTypeConfiguration<SyncNodeGroup>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeGroup> builder)
    {
        builder.ToTable("sync_node_group", Schema);
        builder.HasKey(e => e.GroupId);

        builder.Property(e => e.GroupId).HasColumnName("group_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.GroupName).HasColumnName("group_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
    }
}
