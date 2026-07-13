using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Operations;

namespace MSOSync.App.SignalR;

public sealed class OperationChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<OperationChangedEvent>
{
    public async Task Handle(OperationChangedEvent n, CancellationToken ct)
        => await hub.Clients.Group("operators").SendAsync(
            "OperationsEvent",
            new OperationsEvent(
                Type:           OperationsEventType.OperationChanged,
                NodeId:         n.OperationId.ToString(),
                NodeLabel:      null,
                PreviousStatus: null,
                CurrentStatus:  n.Status,
                OccurredAt:     DateTimeOffset.UtcNow)
            {
                CorrelationId = n.OperationId,
                Trigger       = n.OperationType,
            },
            ct);
}
