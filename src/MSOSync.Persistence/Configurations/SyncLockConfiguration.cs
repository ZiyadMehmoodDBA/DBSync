using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncLockConfiguration : IEntityTypeConfiguration<SyncLock>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncLock> builder)
    {
        builder.ToTable("sync_lock", Schema);
        builder.HasKey(e => e.LockName);

        builder.Property(e => e.LockName).HasColumnName("lock_name").HasColumnType("varchar(200)").HasMaxLength(200).IsUnicode(false);
        builder.Property(e => e.LockOwner).HasColumnName("lock_owner").HasColumnType("varchar(200)").HasMaxLength(200).IsUnicode(false);
        builder.Property(e => e.LockTime).HasColumnName("lock_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.LockExpiry).HasColumnName("lock_expiry").HasColumnType("datetime2(7)");

        // M031 — lock scope (0 = Platform, 1 = Tenant)
        builder.Property(e => e.Scope)
            .HasColumnName("lock_scope")
            .HasColumnType("int")
            .IsRequired()
            .HasDefaultValue(LockScope.Platform);
    }
}
