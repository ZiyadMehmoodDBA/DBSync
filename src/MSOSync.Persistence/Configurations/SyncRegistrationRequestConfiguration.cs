using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRegistrationRequestConfiguration
    : IEntityTypeConfiguration<SyncRegistrationRequest>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRegistrationRequest> builder)
    {
        builder.ToTable("sync_registration_request", Schema);
        builder.HasKey(e => e.RequestId);

        builder.Property(e => e.RequestId)
            .HasColumnName("request_id").ValueGeneratedOnAdd();
        builder.Property(e => e.NodeId)
            .HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50)
            .IsUnicode(false).IsRequired();
        builder.Property(e => e.NodeName)
            .HasColumnName("node_name").HasMaxLength(256).IsRequired();
        builder.Property(e => e.NodeGroup)
            .HasColumnName("node_group").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.SyncUrl)
            .HasColumnName("sync_url").HasColumnType("varchar(255)").HasMaxLength(255).IsUnicode(false);
        builder.Property(e => e.NodeVersion)
            .HasColumnName("node_version").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.DbType)
            .HasColumnName("db_type").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.RequestTime)
            .HasColumnName("request_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.Approved)
            .HasColumnName("approved").HasDefaultValue(false);
        builder.Property(e => e.MetadataJson)
            .HasColumnName("metadata_json");
        builder.Property(e => e.RegistrationType)
            .HasColumnName("registration_type").HasMaxLength(20)
            .HasConversion<string>().IsRequired();
        builder.Property(e => e.Status)
            .HasColumnName("registration_status").HasMaxLength(20)
            .HasConversion<string>().IsRequired();
        builder.Property(e => e.ProcessedAt)
            .HasColumnName("processed_at").HasColumnType("datetime2(7)");
        builder.Property(e => e.ProcessedBy)
            .HasColumnName("processed_by").HasMaxLength(256);
        builder.Property(e => e.RowVersion)
            .HasColumnName("row_version").IsRowVersion();

        builder.HasIndex(e => e.Status).HasDatabaseName("IX_reg_request_status");
        builder.HasIndex(e => new { e.NodeId, e.Status })
            .HasDatabaseName("IX_reg_request_nodeid_status");

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Status })
            .HasDatabaseName("IX_sync_registration_request_TenantId_Status");
    }
}
