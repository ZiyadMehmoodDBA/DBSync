using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.App.Notifications;

/// Best-effort NODE_DECOMMISSIONING notice to the node (spec §4.7 step 4).
/// Failures are logged and swallowed — the drain does not depend on the node hearing this.
public sealed class NodeDecommissionNotifier(
    IHttpClientFactory httpClientFactory,
    AppDbContext db,
    ILogger<NodeDecommissionNotifier> logger) : INotificationHandler<NodeLifecycleChangedEvent>
{
    public async Task Handle(NodeLifecycleChangedEvent evt, CancellationToken ct)
    {
        if (evt.NewState != NodeLifecycleState.Decommissioning) return;

        var syncUrl = await db.Nodes.AsNoTracking()
            .Where(n => n.NodeId == evt.NodeId)
            .Select(n => n.SyncUrl)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(syncUrl)) return;

        try
        {
            using var client = httpClientFactory.CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.PostAsJsonAsync(
                $"{syncUrl.TrimEnd('/')}/api/v1/sync/lifecycle-notice",
                new { type = "NODE_DECOMMISSIONING", nodeId = evt.NodeId }, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex,
                "Best-effort decommission notice to node {NodeId} failed (expected if unreachable)", evt.NodeId);
        }
    }
}
