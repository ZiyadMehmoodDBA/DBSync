namespace MSOSync.Metadata.Notifications;

public interface INotificationQueryService
{
    Task<NotificationPageDto> GetPagedAsync(
        long userId, string? cursor, int pageSize, bool unreadOnly, string? severityFilter, CancellationToken ct);

    Task<int>  GetUnreadCountAsync(long userId, CancellationToken ct);
    Task       MarkReadAsync(long userId, long notificationId, CancellationToken ct);
    Task       MarkAllReadAsync(long userId, CancellationToken ct);
    Task<long> ResolveUserIdAsync(string username, CancellationToken ct);
}
