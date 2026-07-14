using MediatR;
using MSOSync.App.SignalR;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Notifications;

namespace MSOSync.App.Notifications;

public sealed class WorkerFailedNotificationHandler(INotificationService svc)
    : INotificationHandler<WorkerStatusChangedEvent>
{
    public async Task Handle(WorkerStatusChangedEvent n, CancellationToken ct)
    {
        if (n.NewState != WorkerHealthState.Failed) return;
        await svc.CreateAsync(
            NotificationEventType.WorkerFailed, NotificationSeverity.Critical,
            $"Worker '{n.WorkerName}' has failed",
            $"Worker {n.WorkerName} transitioned from {n.PreviousState} to {n.NewState} at {n.OccurredAt:u}.",
            "Worker", n.WorkerName, null,
            NotificationAudience.AllUsers, ct);
    }
}
