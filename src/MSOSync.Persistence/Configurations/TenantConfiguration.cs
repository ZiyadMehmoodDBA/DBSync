using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant", Schema);
        builder.HasKey(e => e.TenantId);

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Slug)
            .HasColumnName("slug")
            .HasColumnType("nvarchar(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(e => e.Slug)
            .IsUnique()
            .HasDatabaseName("UQ_tenant_slug");

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(e => e.Edition)
            .HasColumnName("edition")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(e => e.LicenseId)
            .HasColumnName("license_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.SuspendedAtUtc)
            .HasColumnName("suspended_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired(false);

        builder.Property(e => e.DeletedAtUtc)
            .HasColumnName("deleted_at_utc")
            .HasColumnType("datetimeoffset")
            .IsRequired(false);

        builder.Property(e => e.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion()
            .IsRequired();
    }
}
