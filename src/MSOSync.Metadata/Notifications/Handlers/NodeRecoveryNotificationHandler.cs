using MediatR;
using MSOSync.Metadata.Events;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Notifications.Handlers;

public sealed class NodeRecoveryNotificationHandler(INotificationService svc)
    : INotificationHandler<NodeLifecycleChangedEvent>
{
    public async Task Handle(NodeLifecycleChangedEvent n, CancellationToken ct)
    {
        if (n.NewState != NodeLifecycleState.Recovery) return;
        await svc.CreateAsync(
            NotificationEventType.NodeInRecovery, NotificationSeverity.Warning,
            $"Node '{n.NodeId}' requires recovery approval",
            $"Node {n.NodeId} entered Recovery state (trigger: {n.Trigger}).",
            "Node", n.NodeId, n.CorrelationId.ToString(),
            NotificationAudience.Operators, ct);
    }
}
