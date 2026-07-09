using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Hubs;
using MSOSync.Metadata.Operations;

namespace MSOSync.App.SignalR;

public sealed class OperationChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<OperationChangedEvent>
{
    public async Task Handle(OperationChangedEvent n, CancellationToken ct)
        => await hub.Clients.Group("operators")
            .SendAsync("OperationChanged", new
            {
                operationId   = n.OperationId,
                operationType = n.OperationType,
                status        = n.Status,
            }, ct);
}
