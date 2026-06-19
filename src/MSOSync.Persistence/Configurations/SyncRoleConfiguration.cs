using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncRoleConfiguration : IEntityTypeConfiguration<SyncRole>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncRole> builder)
    {
        builder.ToTable("sync_role", Schema);
        builder.HasKey(e => e.RoleId);

        builder.Property(e => e.RoleId).HasColumnName("role_id").ValueGeneratedOnAdd();
        builder.Property(e => e.RoleName).HasColumnName("role_name").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();

        builder.HasIndex(e => e.RoleName).IsUnique().HasDatabaseName("UQ_sync_role_role_name");
    }
}
