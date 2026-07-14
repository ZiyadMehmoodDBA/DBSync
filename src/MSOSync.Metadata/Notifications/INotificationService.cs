namespace MSOSync.Metadata.Notifications;

public interface INotificationService
{
    Task CreateAsync(
        NotificationEventType eventType,
        NotificationSeverity  severity,
        string                title,
        string                body,
        string?               sourceEntityType,
        string?               sourceEntityId,
        string?               correlationId,
        NotificationAudience  audience,
        CancellationToken     ct = default);
}
