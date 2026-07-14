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
    public Task Handle(NotificationCreatedDomainEvent evt, CancellationToken ct)
        => Task.WhenAll(evt.UserIds.Select(async userId =>
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
                logger.LogWarning(ex,
                    "Failed to push NotificationEvent to user {UserId}", userId);
            }
        }));
}
