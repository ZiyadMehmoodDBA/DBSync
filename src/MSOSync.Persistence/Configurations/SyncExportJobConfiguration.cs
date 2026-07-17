using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncExportJobConfiguration : IEntityTypeConfiguration<SyncExportJob>
{
    public void Configure(EntityTypeBuilder<SyncExportJob> b)
    {
        b.ToTable("sync_export_job");
        b.HasKey(x => x.JobId);

        b.Property(x => x.JobId)          .HasColumnName("job_id")          .HasDefaultValueSql("NEWID()");
        b.Property(x => x.ParentJobId)    .HasColumnName("parent_job_id");
        b.Property(x => x.RequestedBy)    .HasColumnName("requested_by")    .HasMaxLength(256).IsRequired();
        b.Property(x => x.ResourceType)   .HasColumnName("resource_type")   .HasMaxLength(50) .IsRequired();
        b.Property(x => x.Format)         .HasColumnName("format")          .HasMaxLength(10) .IsRequired();
        b.Property(x => x.FiltersJson)    .HasColumnName("filters_json")    .IsRequired();
        b.Property(x => x.Status)         .HasColumnName("status")          .HasMaxLength(20) .IsRequired();
        b.Property(x => x.ProgressPercent).HasColumnName("progress_percent").HasDefaultValue(0);
        b.Property(x => x.RowCount)       .HasColumnName("row_count");
        b.Property(x => x.OutputPath)     .HasColumnName("output_path")     .HasMaxLength(500);
        b.Property(x => x.ErrorMessage)   .HasColumnName("error_message")   .HasMaxLength(1000);
        b.Property(x => x.ExpiresAt)      .HasColumnName("expires_at");
        b.Property(x => x.CreatedAt)      .HasColumnName("created_at")      .HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.StartedAt)      .HasColumnName("started_at");
        b.Property(x => x.CompletedAt)    .HasColumnName("completed_at");

        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_export_job_status_created");
        b.HasIndex(x => new { x.RequestedBy, x.CreatedAt }).HasDatabaseName("IX_export_job_requested_by");

        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        b.HasIndex(x => new { x.TenantId, x.Status })
            .HasDatabaseName("IX_sync_export_job_TenantId_Status");
    }
}
