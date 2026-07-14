using MediatR;
using MSOSync.Metadata.Notifications;
using MSOSync.Persistence;
using MSOSync.Scheduler;

namespace MSOSync.App.Notifications;

public sealed class NodeUnreachableNotificationHandler(INotificationService svc)
    : INotificationHandler<NodeConnectivityChangedEvent>
{
    public async Task Handle(NodeConnectivityChangedEvent n, CancellationToken ct)
    {
        if (n.NewStatus != ConnectivityStatus.Unreachable) return;
        await svc.CreateAsync(
            NotificationEventType.NodeUnreachable, NotificationSeverity.Warning,
            $"Node '{n.NodeId}' is unreachable",
            $"Node {n.NodeId} became unreachable. Previous status: {n.PreviousStatus}.",
            "Node", n.NodeId, null,
            NotificationAudience.AllUsers, ct);
    }
}
