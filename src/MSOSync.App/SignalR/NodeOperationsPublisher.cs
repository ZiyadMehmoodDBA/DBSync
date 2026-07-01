using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Events;
using MSOSync.Scheduler;

namespace MSOSync.App.SignalR;

public sealed class NodeOperationsPublisher(
    IHubContext<OperationsHub> hub)
    : INotificationHandler<NodeMetadataChangedEvent>,
      INotificationHandler<NodeConnectivityChangedEvent>
{
    public async Task Handle(NodeMetadataChangedEvent notification, CancellationToken ct)
    {
        var type = notification.Action switch
        {
            "APPROVED" => OperationsEventType.NodeApproved,
            "REJECTED" => OperationsEventType.NodeRejected,
            "DISABLED" => OperationsEventType.NodeDisabled,
            "ENABLED"  => OperationsEventType.NodeEnabled,
            _          => (OperationsEventType?)null
        };

        if (type is null) return;

        var evt = new OperationsEvent(
            Type:           type.Value,
            NodeId:         notification.NodeId,
            NodeLabel:      null,
            PreviousStatus: null,
            CurrentStatus:  null,
            OccurredAt:     DateTimeOffset.UtcNow);

        await hub.Clients.Group("operators")
            .SendAsync("OperationsEvent", evt, ct);
    }

    public async Task Handle(NodeConnectivityChangedEvent notification, CancellationToken ct)
    {
        var evt = new OperationsEvent(
            Type:           OperationsEventType.NodeHealthChanged,
            NodeId:         notification.NodeId,
            NodeLabel:      null,
            PreviousStatus: notification.PreviousStatus.ToString(),
            CurrentStatus:  notification.NewStatus.ToString(),
            OccurredAt:     DateTimeOffset.UtcNow);

        await hub.Clients.Group("operators")
            .SendAsync("OperationsEvent", evt, ct);
    }
}
