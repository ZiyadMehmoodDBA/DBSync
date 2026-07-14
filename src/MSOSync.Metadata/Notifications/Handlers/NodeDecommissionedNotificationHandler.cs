using MediatR;
using MSOSync.Metadata.Events;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Notifications.Handlers;

public sealed class NodeDecommissionedNotificationHandler(INotificationService svc)
    : INotificationHandler<NodeLifecycleChangedEvent>
{
    public async Task Handle(NodeLifecycleChangedEvent n, CancellationToken ct)
    {
        if (n.NewState != NodeLifecycleState.Decommissioned) return;
        await svc.CreateAsync(
            NotificationEventType.NodeDecommissioned, NotificationSeverity.Info,
            $"Node '{n.NodeId}' decommissioned",
            $"Node {n.NodeId} has been fully decommissioned.",
            "Node", n.NodeId, n.CorrelationId.ToString(),
            NotificationAudience.AllUsers, ct);
    }
}
