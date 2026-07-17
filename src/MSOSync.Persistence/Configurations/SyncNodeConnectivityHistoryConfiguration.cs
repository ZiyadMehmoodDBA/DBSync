using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeConnectivityHistoryConfiguration : IEntityTypeConfiguration<SyncNodeConnectivityHistory>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeConnectivityHistory> builder)
    {
        builder.ToTable("sync_node_connectivity_history", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.PreviousStatus).HasColumnName("previous_status")
            .HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.NewStatus).HasColumnName("new_status")
            .HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.HasOne<SyncNode>().WithMany().HasForeignKey(e => e.NodeId)
            .HasConstraintName("FK_node_connectivity_history_node").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => new { e.NodeId, e.OccurredAt })
            .IsDescending(false, true).HasDatabaseName("IX_node_connectivity_history_node_time");
        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("IX_node_connectivity_history_time");

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.NodeId })
            .HasDatabaseName("IX_sync_node_connectivity_history_TenantId_NodeId");
    }
}
