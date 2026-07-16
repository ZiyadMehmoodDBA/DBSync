using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserPreferenceConfiguration
    : IEntityTypeConfiguration<SyncUserPreference>
{
    public void Configure(EntityTypeBuilder<SyncUserPreference> builder)
    {
        builder.ToTable("sync_user_preference", "msosync");
        builder.HasKey(p => p.PreferenceId);
        builder.Property(p => p.PreferenceId).ValueGeneratedOnAdd();
        builder.Property(p => p.PreferenceKey)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(p => p.PreferenceValue)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.Property(p => p.UpdatedAt)
            .HasColumnType("datetime2(7)")
            .HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(p => new { p.UserId, p.PreferenceKey })
            .IsUnique()
            .HasDatabaseName("IX_sync_user_preference_user_key");
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // M031 — hybrid tenancy (NULL = global preference)
        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired(false);
    }
}
