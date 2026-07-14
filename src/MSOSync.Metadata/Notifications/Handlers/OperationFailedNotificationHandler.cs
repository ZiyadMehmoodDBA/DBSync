using MediatR;
using MSOSync.Metadata.Operations;

namespace MSOSync.Metadata.Notifications.Handlers;

public sealed class OperationFailedNotificationHandler(INotificationService svc)
    : INotificationHandler<OperationChangedEvent>
{
    public async Task Handle(OperationChangedEvent n, CancellationToken ct)
    {
        if (n.Status != "Failed") return;
        await svc.CreateAsync(
            NotificationEventType.OperationFailed, NotificationSeverity.Warning,
            $"Operation '{n.OperationType}' failed",
            $"Operation {n.OperationId} ({n.OperationType}) failed.",
            "Operation", n.OperationId.ToString(), null,
            NotificationAudience.Operators, ct);
    }
}
