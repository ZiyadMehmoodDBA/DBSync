using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncAuditConfiguration : IEntityTypeConfiguration<SyncAudit>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncAudit> builder)
    {
        builder.ToTable("sync_audit", Schema);
        builder.HasKey(e => e.AuditId);

        builder.Property(e => e.AuditId).HasColumnName("audit_id").ValueGeneratedOnAdd();
        builder.Property(e => e.Username).HasColumnName("username").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ActionName).HasColumnName("action_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.ObjectName).HasColumnName("object_name").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasColumnType("varchar(100)").HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.CreateTime).HasColumnName("create_time").HasColumnType("datetime2(7)");

        builder.HasIndex(e => e.CreateTime).HasDatabaseName("IX_sync_audit_create_time");
        builder.HasIndex(e => e.Username).HasDatabaseName("IX_sync_audit_username");
    }
}
