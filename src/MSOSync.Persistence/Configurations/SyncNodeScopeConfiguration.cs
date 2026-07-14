using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeScopeConfiguration : IEntityTypeConfiguration<SyncNodeScope>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeScope> builder)
    {
        builder.ToTable("sync_node_scope", Schema);
        builder.HasKey(e => e.NodeId);

        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SyncDirection)
            .HasColumnName("sync_direction")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(e => e.InitialLoadPolicy)
            .HasColumnName("initial_load_policy")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(e => e.CreatedTime).HasColumnName("created_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.UpdatedTime).HasColumnName("updated_time").HasColumnType("datetime2(7)");
    }
}
