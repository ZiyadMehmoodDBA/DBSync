using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNodeConfigurationHistoryConfiguration
    : IEntityTypeConfiguration<SyncNodeConfigurationHistory>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNodeConfigurationHistory> builder)
    {
        builder.ToTable("sync_node_configuration_history", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("NEWID()");
        builder.Property(e => e.NodeId).HasColumnName("node_id")
            .HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type")
            .HasColumnType("nvarchar(50)").HasMaxLength(50).IsRequired();
        builder.Property(e => e.TemplateId).HasColumnName("template_id");
        builder.Property(e => e.TemplateVersion).HasColumnName("template_version");
        builder.Property(e => e.ConfigurationHash).HasColumnName("configuration_hash")
            .HasColumnType("nvarchar(64)").HasMaxLength(64);
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id")
            .HasColumnType("nvarchar(64)").HasMaxLength(64);
        builder.Property(e => e.ActorId).HasColumnName("actor_id");
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.Notes).HasColumnName("notes")
            .HasColumnType("nvarchar(500)").HasMaxLength(500);

        builder.HasIndex(e => new { e.NodeId, e.OccurredAt })
            .HasDatabaseName("IX_node_config_history_node_time");
    }
}
