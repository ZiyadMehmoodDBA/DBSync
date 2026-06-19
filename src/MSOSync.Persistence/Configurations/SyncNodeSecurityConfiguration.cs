using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeSecurityConfiguration : IEntityTypeConfiguration<SyncNodeSecurity>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeSecurity> builder)
    {
        builder.ToTable("sync_node_security", Schema);
        builder.HasKey(e => e.NodeId);

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.NodeToken).HasColumnName("node_token").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false).IsRequired();
        builder.Property(e => e.CreatedTime).HasColumnName("created_time").HasColumnType("datetime2(7)");
    }
}
