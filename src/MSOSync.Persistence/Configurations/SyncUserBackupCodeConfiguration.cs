using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserBackupCodeConfiguration : IEntityTypeConfiguration<SyncUserBackupCode>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUserBackupCode> builder)
    {
        builder.ToTable("sync_user_backup_code", Schema);
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.CodeHash).HasColumnName("code_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.IsUsed).HasColumnName("is_used").HasDefaultValue(false);
        builder.Property(e => e.UsedAt).HasColumnName("used_at").HasColumnType("datetime2(7)");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .HasConstraintName("FK_sync_user_backup_code_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId).HasDatabaseName("IX_sync_user_backup_code_user_id");
    }
}
