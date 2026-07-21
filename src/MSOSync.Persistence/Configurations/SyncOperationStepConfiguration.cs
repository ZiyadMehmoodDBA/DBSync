using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncOperationStepConfiguration : IEntityTypeConfiguration<SyncOperationStep>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncOperationStep> b)
    {
        b.ToTable("sync_operation_step", Schema);
        b.HasKey(x => x.StepId);
        b.Property(x => x.StepId).HasColumnName("step_id").ValueGeneratedNever();
        b.Property(x => x.OperationId).HasColumnName("operation_id");
        b.Property(x => x.NodeId).HasColumnName("node_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.WaveNumber).HasColumnName("wave_number");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(30).IsRequired();
        b.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("datetime2(7)");
        b.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("datetime2(7)");
        b.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

        b.HasOne<SyncOperation>().WithMany().HasForeignKey(x => x.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.OperationId, x.WaveNumber })
            .HasDatabaseName("ix_sync_operation_step_op_wave");
        b.HasIndex(x => new { x.TenantId, x.NodeId })
            .HasDatabaseName("ix_sync_operation_step_tenant_node");
    }
}
