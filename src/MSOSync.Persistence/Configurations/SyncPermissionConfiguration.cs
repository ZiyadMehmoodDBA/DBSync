using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncPermissionConfiguration : IEntityTypeConfiguration<SyncPermission>
{
    public void Configure(EntityTypeBuilder<SyncPermission> builder)
    {
        builder.ToTable("sync_permission", "msosync");
        builder.HasKey(p => p.PermissionKey);
        builder.Property(p => p.PermissionKey).HasMaxLength(50).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(255);
        builder.Property(p => p.Category).HasMaxLength(50).IsRequired();
        builder.Property(p => p.SortOrder).HasDefaultValue(0);
        builder.Property(p => p.IsSystem).HasDefaultValue(true);
    }
}
