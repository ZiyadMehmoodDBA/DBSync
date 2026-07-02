// src/MSOSync.App/SignalR/PermissionChangedPublisher.cs
using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Permissions;

namespace MSOSync.App.SignalR;

public sealed class PermissionChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<PermissionChangedNotification>
{
    public async Task Handle(PermissionChangedNotification n, CancellationToken ct)
    {
        await hub.Clients.All.SendAsync("PermissionEvent", new
        {
            roleName   = n.RoleName,
            action     = n.Action,
            occurredAt = n.OccurredAt,
        }, ct);
    }
}
