using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.App.Export;
using MSOSync.App.Hubs;

namespace MSOSync.App.SignalR;

public sealed class ExportJobChangedPublisher(IHubContext<OperationsHub> hub)
    : INotificationHandler<ExportJobChangedNotification>
{
    public async Task Handle(ExportJobChangedNotification n, CancellationToken ct)
    {
        await hub.Clients.User(n.RequestedBy).SendAsync("ExportJobEvent", new
        {
            jobId           = n.JobId,
            status          = n.Status,
            progressPercent = n.ProgressPercent,
            rowCount        = n.RowCount,
        }, ct);
    }
}
