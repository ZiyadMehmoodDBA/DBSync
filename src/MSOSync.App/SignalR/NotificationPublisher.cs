using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Notifications;

namespace MSOSync.App.SignalR;

public sealed class NotificationPublisher(
    IHubContext<OperationsHub> hub,
    ILogger<NotificationPublisher> logger)
    : INotificationHandler<NotificationCreatedDomainEvent>
{
    public async Task Handle(NotificationCreatedDomainEvent evt, CancellationToken ct)
    {
        foreach (var userId in evt.UserIds)
        {
            var dto = evt.PushDto with
            {
                UnreadCount = evt.UnreadCounts.GetValueOrDefault(userId, 1)
            };

            try
            {
                await hub.Clients
                    .Group($"user-{userId}")
                    .SendAsync("NotificationEvent", dto, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to push notification {NotificationId} to user {UserId}",
                    evt.NotificationId, userId);
            }
        }
    }
}
