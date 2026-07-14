// src/MSOSync.Persistence/Entities/SyncNotification.cs
namespace MSOSync.Persistence.Entities;

public sealed class SyncNotification
{
    public long    NotificationId   { get; set; }
    public string  EventType        { get; set; } = null!;   // NotificationEventType enum name
    public string  Severity         { get; set; } = null!;   // NotificationSeverity enum name
    public string  Title            { get; set; } = null!;
    public string  Body             { get; set; } = null!;
    public string? SourceEntityType { get; set; }
    public string? SourceEntityId   { get; set; }
    public string? DedupKey         { get; set; }
    public int     OccurrenceCount  { get; set; } = 1;
    public string? CorrelationId    { get; set; }
    public DateTime CreatedAt       { get; set; }
    public DateTime LastOccurredAt  { get; set; }

    public ICollection<SyncUserNotification> UserNotifications { get; set; } = [];
}
