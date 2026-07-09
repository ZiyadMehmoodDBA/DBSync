using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;

namespace MSOSync.App.SignalR;

public sealed class WorkerStatusChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<WorkerStatusChangedEvent>
{
    public async Task Handle(WorkerStatusChangedEvent n, CancellationToken ct)
        => await hub.Clients.Group("operators").SendAsync(
            "WorkerStatusChanged",
            new
            {
                n.WorkerName,
                PreviousState = n.PreviousState.ToString(),
                NewState = n.NewState.ToString(),
                n.OccurredAt
            },
            ct);
}
