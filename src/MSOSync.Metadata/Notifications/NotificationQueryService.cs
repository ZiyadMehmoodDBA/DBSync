using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Notifications;

public sealed class NotificationQueryService(AppDbContext db, CursorSigner signer)
    : INotificationQueryService
{
    public async Task<NotificationPageDto> GetPagedAsync(
        long userId, string? cursor, int pageSize, bool unreadOnly, string? severityFilter, CancellationToken ct)
    {
        var q = db.UserNotifications
            .AsNoTracking()
            .Where(un => un.UserId == userId);

        if (unreadOnly)
            q = q.Where(un => !un.IsRead);

        if (!string.IsNullOrEmpty(severityFilter))
            q = q.Where(un => un.Notification.Severity == severityFilter);

        if (cursor is not null)
        {
            var (cursorId, _) = signer.Decode(cursor);
            q = q.Where(un => un.NotificationId < cursorId);
        }

        var rows = await q
            .OrderByDescending(un => un.NotificationId)
            .Take(pageSize + 1)
            .Select(un => new NotificationDto(
                un.NotificationId,
                Enum.Parse<NotificationEventType>(un.Notification.EventType),
                Enum.Parse<NotificationSeverity>(un.Notification.Severity),
                un.Notification.Title,
                un.Notification.Body,
                un.Notification.SourceEntityType,
                un.Notification.SourceEntityId,
                un.Notification.CorrelationId,
                un.Notification.CreatedAt,
                un.Notification.LastOccurredAt,
                un.Notification.OccurrenceCount,
                un.IsRead,
                un.ReadAt))
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows = rows.Take(pageSize).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[^1];
            nextCursor = signer.Encode(last.NotificationId, last.CreatedAt.Ticks);
        }

        var totalUnread = await db.UserNotifications
            .AsNoTracking()
            .CountAsync(un => un.UserId == userId && !un.IsRead, ct);

        return new NotificationPageDto(rows.AsReadOnly(), nextCursor, totalUnread);
    }

    public Task<int> GetUnreadCountAsync(long userId, CancellationToken ct)
        => db.UserNotifications
            .AsNoTracking()
            .CountAsync(un => un.UserId == userId && !un.IsRead, ct);

    public async Task MarkReadAsync(long userId, long notificationId, CancellationToken ct)
    {
        var row = await db.UserNotifications
            .FindAsync([userId, notificationId], ct);
        if (row is null || row.IsRead) return;

        row.IsRead = true;
        row.ReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(long userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.UserNotifications
            .Where(un => un.UserId == userId && !un.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(un => un.IsRead, true)
                .SetProperty(un => un.ReadAt, now), ct);
    }

    public async Task<long> ResolveUserIdAsync(string username, CancellationToken ct)
    {
        var userId = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => (long?)u.UserId)
            .FirstOrDefaultAsync(ct);

        if (userId is null)
            throw new NotFoundException($"User '{username}' not found");

        return userId.Value;
    }
}
