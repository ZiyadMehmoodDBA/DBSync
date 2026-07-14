using MediatR;
using MSOSync.App.SignalR;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Notifications;

namespace MSOSync.App.Notifications;

public sealed class WorkerWarningNotificationHandler(INotificationService svc)
    : INotificationHandler<WorkerStatusChangedEvent>
{
    public async Task Handle(WorkerStatusChangedEvent n, CancellationToken ct)
    {
        if (n.NewState != WorkerHealthState.Warning) return;
        await svc.CreateAsync(
            NotificationEventType.WorkerWarning, NotificationSeverity.Warning,
            $"Worker '{n.WorkerName}' is degraded",
            $"Worker {n.WorkerName} entered Warning state at {n.OccurredAt:u}.",
            "Worker", n.WorkerName, null,
            NotificationAudience.Operators, ct);
    }
}
