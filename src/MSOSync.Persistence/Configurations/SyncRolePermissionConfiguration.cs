using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRolePermissionConfiguration : IEntityTypeConfiguration<SyncRolePermission>
{
    public void Configure(EntityTypeBuilder<SyncRolePermission> builder)
    {
        builder.ToTable("sync_role_permission", "msosync");
        builder.HasKey(p => new { p.RoleName, p.PermissionKey });
        builder.Property(p => p.RoleName).HasMaxLength(50).IsRequired();
        builder.Property(p => p.PermissionKey).HasMaxLength(50).IsRequired();

        builder.HasOne(p => p.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(p => p.PermissionKey)
            .OnDelete(DeleteBehavior.Cascade);
        // RoleName FK to sync_role.role_name is enforced in migration SQL (no EF navigation needed)
    }
}
