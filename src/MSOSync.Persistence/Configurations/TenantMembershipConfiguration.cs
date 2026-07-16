using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_membership", Schema);
        builder.HasKey(e => new { e.TenantId, e.UserId });

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(e => e.RoleId)
            .HasColumnName("role_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasColumnType("int")
            .IsRequired();

        builder.Property(e => e.JoinedAt)
            .HasColumnName("joined_at")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.LastAccessedAt)
            .HasColumnName("last_accessed_at")
            .HasColumnType("datetimeoffset")
            .IsRequired();

        builder.Property(e => e.RowVersion)
            .HasColumnName("row_version")
            .IsRowVersion()
            .IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .HasConstraintName("FK_tenant_membership_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("FK_tenant_membership_user_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Role)
            .WithMany()
            .HasForeignKey(e => e.RoleId)
            .HasConstraintName("FK_tenant_membership_role_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("IX_tenant_membership_tenant_id");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_tenant_membership_user_id");
    }
}
