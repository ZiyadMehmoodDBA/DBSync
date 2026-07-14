using MediatR;
using MSOSync.Metadata.Events;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Notifications.Handlers;

public sealed class NodeRejectedNotificationHandler(INotificationService svc)
    : INotificationHandler<NodeLifecycleChangedEvent>
{
    public async Task Handle(NodeLifecycleChangedEvent n, CancellationToken ct)
    {
        if (n.NewState != NodeLifecycleState.Rejected) return;
        await svc.CreateAsync(
            NotificationEventType.NodeRejected, NotificationSeverity.Info,
            $"Node '{n.NodeId}' registration rejected",
            $"Node {n.NodeId} registration was rejected.",
            "Node", n.NodeId, n.CorrelationId.ToString(),
            NotificationAudience.AllUsers, ct);
    }
}
