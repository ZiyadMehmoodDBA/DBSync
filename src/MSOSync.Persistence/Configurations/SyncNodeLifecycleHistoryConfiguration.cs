using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeLifecycleHistoryConfiguration : IEntityTypeConfiguration<SyncNodeLifecycleHistory>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeLifecycleHistory> builder)
    {
        builder.ToTable("sync_node_lifecycle_history", Schema);
        builder.HasKey(e => e.HistoryId);
        builder.Property(e => e.HistoryId).HasColumnName("history_id").UseIdentityColumn();
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.FromState).HasColumnName("from_state")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>();
        builder.Property(e => e.ToState).HasColumnName("to_state")
            .HasColumnType("varchar(30)").HasMaxLength(30).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.Trigger).HasColumnName("trigger")
            .HasColumnType("varchar(20)").HasMaxLength(20).IsUnicode(false).HasConversion<string>().IsRequired();
        builder.Property(e => e.Reason).HasColumnName("reason")
            .HasColumnType("nvarchar(512)").HasMaxLength(512);
        builder.Property(e => e.Actor).HasColumnName("actor")
            .HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        builder.Property(e => e.MetadataJson).HasColumnName("metadata_json").HasColumnType("nvarchar(max)");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.HasOne<SyncNode>().WithMany().HasForeignKey(e => e.NodeId)
            .HasConstraintName("FK_node_lifecycle_history_node").OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => new { e.NodeId, e.OccurredAt })
            .IsDescending(false, true).HasDatabaseName("IX_node_lifecycle_history_node_time");
        builder.HasIndex(e => e.CorrelationId)
            .HasDatabaseName("IX_node_lifecycle_history_correlation_id");
    }
}
