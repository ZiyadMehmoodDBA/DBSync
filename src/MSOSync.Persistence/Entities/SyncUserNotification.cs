// src/MSOSync.Persistence/Entities/SyncUserNotification.cs
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncUserNotification
{
    public long     UserId         { get; set; }
    public long     NotificationId { get; set; }
    public bool     IsRead         { get; set; }
    public DateTime? ReadAt        { get; set; }
    public bool     IsArchived     { get; set; }
    public DateTime? ArchivedAt    { get; set; }

    public SyncUser         User         { get; set; } = null!;
    public SyncNotification Notification { get; set; } = null!;
}
