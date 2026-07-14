using MediatR;
using MSOSync.Metadata.Notifications;
using MSOSync.Scheduler;

namespace MSOSync.App.Notifications;

public sealed class SchedulerRecoveryNotificationHandler(INotificationService svc)
    : INotificationHandler<SchedulerRecoveryEvent>
{
    public async Task Handle(SchedulerRecoveryEvent n, CancellationToken ct)
    {
        await svc.CreateAsync(
            NotificationEventType.SchedulerRecovered, NotificationSeverity.Warning,
            "Scheduler recovered from downtime",
            $"The scheduler recovered. Recovered: {n.SentRecovered + n.NewRecovered}, Requeued: {n.RetryRequeued}.",
            "Worker", "Scheduler", null,
            NotificationAudience.Administrators, ct);
    }
}
