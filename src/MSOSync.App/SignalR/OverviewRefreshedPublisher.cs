using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Events;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Overview;

namespace MSOSync.App.SignalR;

/// <summary>
/// Invalidates the overview snapshot cache and broadcasts a SignalR refresh signal
/// whenever a relevant domain event changes system state.
/// </summary>
public sealed class OverviewRefreshedPublisher(
    IHubContext<OperationsHub> hub,
    OverviewSnapshotCache cache)
    : INotificationHandler<WorkerStatusChangedEvent>,
      INotificationHandler<NodeLifecycleChangedEvent>,
      INotificationHandler<OperationChangedEvent>,
      INotificationHandler<ConfigurationChangedEvent>
{
    public async Task Handle(WorkerStatusChangedEvent notification, CancellationToken cancellationToken)
        => await InvalidateAndNotifyAsync(cancellationToken);

    public async Task Handle(NodeLifecycleChangedEvent notification, CancellationToken cancellationToken)
        => await InvalidateAndNotifyAsync(cancellationToken);

    public async Task Handle(OperationChangedEvent notification, CancellationToken cancellationToken)
        => await InvalidateAndNotifyAsync(cancellationToken);

    public async Task Handle(ConfigurationChangedEvent notification, CancellationToken cancellationToken)
        => await InvalidateAndNotifyAsync(cancellationToken);

    private async Task InvalidateAndNotifyAsync(CancellationToken ct)
    {
        await cache.InvalidateAsync(ct);
        await hub.Clients.Group("operators")
            .SendAsync("OverviewRefreshed", null, ct);
    }
}
