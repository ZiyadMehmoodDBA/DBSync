using MediatR;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Notifications;

public sealed class NotificationService(AppDbContext db, IPublisher publisher) : INotificationService
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(10);

    public async Task CreateAsync(
        NotificationEventType eventType,
        NotificationSeverity  severity,
        string                title,
        string                body,
        string?               sourceEntityType,
        string?               sourceEntityId,
        string?               correlationId,
        NotificationAudience  audience,
        CancellationToken     ct = default)
    {
        string? dedupKey = sourceEntityId is not null ? $"{eventType}:{sourceEntityId}" : null;

        if (dedupKey is not null)
        {
            var cutoff   = DateTime.UtcNow.Subtract(DedupWindow);
            var existing = await db.Notifications
                .Where(n => n.DedupKey == dedupKey && n.CreatedAt >= cutoff)
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                existing.OccurrenceCount++;
                existing.LastOccurredAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        var now = DateTime.UtcNow;
        var notification = new SyncNotification
        {
            EventType        = eventType.ToString(),
            Severity         = severity.ToString(),
            Title            = title,
            Body             = body,
            SourceEntityType = sourceEntityType,
            SourceEntityId   = sourceEntityId,
            DedupKey         = dedupKey,
            CorrelationId    = correlationId,
            CreatedAt        = now,
            LastOccurredAt   = now,
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        var userIds = await ResolveUserIdsAsync(audience, ct);
        if (userIds.Count == 0) return;

        db.UserNotifications.AddRange(userIds.Select(uid => new SyncUserNotification
        {
            UserId         = uid,
            NotificationId = notification.NotificationId,
        }));
        await db.SaveChangesAsync(ct);

        // Compute per-user unread count in one query
        var unreadCounts = await db.UserNotifications
            .AsNoTracking()
            .Where(n => userIds.Contains(n.UserId) && !n.IsRead)
            .GroupBy(n => n.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var pushDto = new NotificationPushDto(
            NotificationId:   notification.NotificationId,
            EventType:        eventType,
            Severity:         severity,
            Title:            title,
            Body:             body,
            SourceEntityType: sourceEntityType,
            SourceEntityId:   sourceEntityId,
            CreatedAt:        now,
            UnreadCount:      0);  // overridden per-user by NotificationPublisher

        await publisher.Publish(
            new NotificationCreatedDomainEvent(
                notification.NotificationId,
                userIds,
                pushDto,
                unreadCounts),
            ct);
    }

    private async Task<IReadOnlyList<long>> ResolveUserIdsAsync(
        NotificationAudience audience, CancellationToken ct)
    {
        var baseQuery =
            from u in db.Users
            where u.Enabled == true
            select u;

        var filtered = audience switch
        {
            NotificationAudience.Operators =>
                from u  in baseQuery
                join ur in db.UserRoles on u.UserId equals ur.UserId
                join r  in db.Roles    on ur.RoleId  equals r.RoleId
                where r.RoleName == "OPERATOR" || r.RoleName == "ADMIN"
                select u.UserId,

            NotificationAudience.Administrators =>
                from u  in baseQuery
                join ur in db.UserRoles on u.UserId equals ur.UserId
                join r  in db.Roles    on ur.RoleId  equals r.RoleId
                where r.RoleName == "ADMIN"
                select u.UserId,

            _ => baseQuery.Select(u => u.UserId)
        };

        return await filtered.Distinct().ToListAsync(ct);
    }
}
