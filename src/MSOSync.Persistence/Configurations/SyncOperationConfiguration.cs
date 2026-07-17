using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncOperationConfiguration : IEntityTypeConfiguration<SyncOperation>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncOperation> b)
    {
        b.ToTable("sync_operation", Schema);
        b.HasKey(x => x.OperationId);

        b.Property(x => x.OperationId)
            .HasColumnName("operation_id")
            .HasDefaultValueSql("NEWID()");

        b.Property(x => x.OperationType)
            .HasColumnName("operation_type")
            .HasColumnType("varchar(50)")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        b.Property(x => x.ReferenceId)
            .HasColumnName("reference_id");

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        b.Property(x => x.Result)
            .HasColumnName("result")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsUnicode(false);

        b.Property(x => x.Source)
            .HasColumnName("source")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        b.Property(x => x.ProgressPercent)
            .HasColumnName("progress_percent");

        b.Property(x => x.ProgressMessage)
            .HasColumnName("progress_message")
            .HasColumnType("varchar(500)")
            .HasMaxLength(500)
            .IsUnicode(false);

        b.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsUnicode(false);

        b.Property(x => x.InitiatedBy)
            .HasColumnName("initiated_by");

        b.Property(x => x.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("nvarchar(2000)")
            .HasMaxLength(2000);

        b.Property(x => x.Summary)
            .HasColumnName("summary")
            .HasColumnType("varchar(500)")
            .HasMaxLength(500)
            .IsUnicode(false);

        b.Property(x => x.CanCancel)
            .HasColumnName("can_cancel")
            .HasDefaultValue(false);

        b.Property(x => x.CanRetry)
            .HasColumnName("can_retry")
            .HasDefaultValue(false);

        b.Property(x => x.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("datetime2(7)")
            .IsRequired();

        b.Property(x => x.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("datetime2(7)");

        // Indexes
        b.HasIndex(x => x.Status)
            .HasDatabaseName("IX_sync_operation_status");

        b.HasIndex(x => x.OperationType)
            .HasDatabaseName("IX_sync_operation_type");

        b.HasIndex(x => x.StartedAt)
            .IsDescending(true)
            .HasDatabaseName("IX_sync_operation_started_at_desc");

        b.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_sync_operation_correlation_id");

        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("IX_sync_operation_TenantId_Status");
    }
}
