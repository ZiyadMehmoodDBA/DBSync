using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncParameterConfiguration : IEntityTypeConfiguration<SyncParameter>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncParameter> builder)
    {
        builder.ToTable("sync_parameter", Schema);
        // Surrogate PK with identity — enables (ParameterName, TenantId) unique index
        // so tenant overrides can coexist alongside platform defaults (TenantId=null).
        // M031 migration (Task 7) adds TenantId column + unique index to the real DB.
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityColumn();

        builder.Property(e => e.ParameterName)
            .HasColumnName("parameter_name")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsUnicode(false);

        builder.Property(e => e.ParameterValue)
            .HasColumnName("parameter_value")
            .HasColumnType("nvarchar(max)");

        // TenantId: null = platform default; non-null = tenant override.
        // Column added by M031 migration (Task 7).
        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id");

        // Unique index: one record per (name, tenant) pair.
        builder.HasIndex(e => new { e.ParameterName, e.TenantId })
            .IsUnique()
            .HasDatabaseName("UX_sync_parameter_name_tenant");

        // ── M025: metadata columns ─────────────────────────────────────────────

        builder.Property(e => e.Category)
            .HasColumnName("category")
            .HasColumnType("varchar(50)")
            .HasMaxLength(50)
            .IsUnicode(false);

        builder.Property(e => e.DisplayName)
            .HasColumnName("display_name")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasColumnName("description")
            .HasColumnType("nvarchar(1000)")
            .HasMaxLength(1000);

        builder.Property(e => e.DisplayOrder)
            .HasColumnName("display_order");

        builder.Property(e => e.ValueType)
            .HasColumnName("value_type")
            .HasColumnType("varchar(30)")
            .HasMaxLength(30)
            .IsUnicode(false);

        builder.Property(e => e.MinimumValue)
            .HasColumnName("minimum_value")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsUnicode(false);

        builder.Property(e => e.MaximumValue)
            .HasColumnName("maximum_value")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsUnicode(false);

        builder.Property(e => e.AllowedValues)
            .HasColumnName("allowed_values")
            .HasColumnType("nvarchar(max)");

        builder.Property(e => e.DependsOn)
            .HasColumnName("depends_on")
            .HasColumnType("varchar(200)")
            .HasMaxLength(200)
            .IsUnicode(false);

        builder.Property(e => e.ConflictsWith)
            .HasColumnName("conflicts_with")
            .HasColumnType("varchar(200)")
            .HasMaxLength(200)
            .IsUnicode(false);
    }
}
