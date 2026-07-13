using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;

namespace MSOSync.App.SignalR;

public sealed class WorkerStatusChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<WorkerStatusChangedEvent>
{
    public async Task Handle(WorkerStatusChangedEvent n, CancellationToken ct)
        => await hub.Clients.Group("operators").SendAsync(
            "OperationsEvent",
            new OperationsEvent(
                Type:           OperationsEventType.WorkerStatusChanged,
                NodeId:         n.WorkerName,
                NodeLabel:      n.WorkerName,
                PreviousStatus: n.PreviousState.ToString(),
                CurrentStatus:  n.NewState.ToString(),
                OccurredAt:     n.OccurredAt),
            ct);
}
