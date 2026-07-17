// src/MSOSync.Persistence/Configurations/SyncNotificationConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncNotificationConfiguration : IEntityTypeConfiguration<SyncNotification>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncNotification> b)
    {
        b.ToTable("sync_notification", Schema);
        b.HasKey(x => x.NotificationId);

        b.Property(x => x.NotificationId)
            .HasColumnName("notification_id")
            .ValueGeneratedOnAdd();

        b.Property(x => x.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.Severity)
            .HasColumnName("severity")
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.Body)
            .HasColumnName("body")
            .HasMaxLength(1000)
            .IsRequired();

        b.Property(x => x.SourceEntityType)
            .HasColumnName("source_entity_type")
            .HasMaxLength(50);

        b.Property(x => x.SourceEntityId)
            .HasColumnName("source_entity_id")
            .HasMaxLength(200);

        b.Property(x => x.DedupKey)
            .HasColumnName("dedup_key")
            .HasMaxLength(260);

        b.Property(x => x.OccurrenceCount)
            .HasColumnName("occurrence_count")
            .HasDefaultValue(1);

        b.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasColumnType("varchar(100)")
            .HasMaxLength(100)
            .IsUnicode(false);

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        b.Property(x => x.LastOccurredAt)
            .HasColumnName("last_occurred_at")
            .HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasMany(x => x.UserNotifications)
            .WithOne(x => x.Notification)
            .HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.DedupKey, x.CreatedAt })
            .HasDatabaseName("IX_sn_dedup")
            .HasFilter("[dedup_key] IS NOT NULL");

        b.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uniqueidentifier")
            .IsRequired();

        b.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .HasDatabaseName("IX_sync_notification_TenantId_CreatedAt");
    }
}
