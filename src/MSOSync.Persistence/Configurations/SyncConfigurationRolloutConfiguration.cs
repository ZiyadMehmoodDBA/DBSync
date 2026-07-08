using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncConfigurationRolloutConfiguration
    : IEntityTypeConfiguration<SyncConfigurationRollout>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncConfigurationRollout> builder)
    {
        builder.ToTable("sync_configuration_rollout", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("NEWID()");
        builder.Property(e => e.Status).HasColumnName("status")
            .HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.TemplateId).HasColumnName("template_id").IsRequired();
        builder.Property(e => e.TemplateVersion).HasColumnName("template_version").IsRequired();
        builder.Property(e => e.TargetNodeCount).HasColumnName("target_node_count").IsRequired();
        builder.Property(e => e.AppliedCount).HasColumnName("applied_count").HasDefaultValue(0);
        builder.Property(e => e.FailedCount).HasColumnName("failed_count").HasDefaultValue(0);
        builder.Property(e => e.PendingCount).HasColumnName("pending_count").HasDefaultValue(0);
        builder.Property(e => e.ProgressPercent).HasColumnName("progress_percent").HasDefaultValue(0);
        builder.Property(e => e.InitiatedBy).HasColumnName("initiated_by").IsRequired();
        builder.Property(e => e.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(e => e.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(e => e.Status).HasDatabaseName("IX_rollout_status");
    }
}
