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

        builder.Property(e => e.LockName).HasColumnName("lock_name").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.LockOwner).HasColumnName("lock_owner").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.LockTime).HasColumnName("lock_time").HasColumnType("datetime2(7)");
    }
}
