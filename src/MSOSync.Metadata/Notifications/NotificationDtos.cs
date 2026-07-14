namespace MSOSync.Metadata.Notifications;

public sealed record NotificationDto(
    long                  NotificationId,
    NotificationEventType EventType,
    NotificationSeverity  Severity,
    string                Title,
    string                Body,
    string?               SourceEntityType,
    string?               SourceEntityId,
    string?               CorrelationId,
    DateTime              CreatedAt,
    DateTime              LastOccurredAt,
    int                   OccurrenceCount,
    bool                  IsRead,
    DateTime?             ReadAt);

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationDto> Items,
    string? NextCursor,
    int     TotalUnread);

public sealed record NotificationPushDto(
    long                  NotificationId,
    NotificationEventType EventType,
    NotificationSeverity  Severity,
    string                Title,
    string                Body,
    string?               SourceEntityType,
    string?               SourceEntityId,
    DateTime              CreatedAt,
    int                   UnreadCount);
