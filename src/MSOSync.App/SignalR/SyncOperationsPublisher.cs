using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Engine;

namespace MSOSync.App.SignalR;

public sealed class SyncOperationsPublisher(
    IHubContext<OperationsHub> hub)
    : INotificationHandler<SyncCycleCompletedEvent>
{
    public async Task Handle(SyncCycleCompletedEvent notification, CancellationToken ct)
    {
        var evt = new OperationsEvent(
            Type:           OperationsEventType.SyncCycleCompleted,
            NodeId:         "system",
            NodeLabel:      null,
            PreviousStatus: null,
            CurrentStatus:  null,
            OccurredAt:     DateTimeOffset.UtcNow,
            GroupId:        "global");

        await hub.Clients.Group("operators")
            .SendAsync("OperationsEvent", evt, ct);
    }
}
