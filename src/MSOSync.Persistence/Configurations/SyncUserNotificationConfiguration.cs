// src/MSOSync.Persistence/Configurations/SyncUserNotificationConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncUserNotificationConfiguration : IEntityTypeConfiguration<SyncUserNotification>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncUserNotification> b)
    {
        b.ToTable("sync_user_notification", Schema);
        b.HasKey(x => new { x.UserId, x.NotificationId });

        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.NotificationId).HasColumnName("notification_id");

        b.Property(x => x.IsRead)
            .HasColumnName("is_read")
            .HasDefaultValue(false);

        b.Property(x => x.ReadAt).HasColumnName("read_at");

        b.Property(x => x.IsArchived)
            .HasColumnName("is_archived")
            .HasDefaultValue(false);

        b.Property(x => x.ArchivedAt).HasColumnName("archived_at");

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.Notification)
            .WithMany(x => x.UserNotifications)
            .HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.UserId, x.IsRead, x.NotificationId })
            .HasDatabaseName("IX_sun_user_unread");

        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        b.HasIndex(x => new { x.TenantId, x.UserId })
            .HasDatabaseName("IX_sync_user_notification_TenantId_UserId");
    }
}
