using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserRoleConfiguration : IEntityTypeConfiguration<SyncUserRole>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUserRole> builder)
    {
        builder.ToTable("sync_user_role", Schema);
        builder.HasKey(e => new { e.UserId, e.RoleId });

        builder.Property(e => e.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(e => e.RoleId).HasColumnName("role_id").ValueGeneratedNever();
    }
}
