using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Topology;

public sealed class TopologyService(AppDbContext db) : ITopologyService
{
    public async Task<IReadOnlyList<SourceNodeInfo>> GetSourceNodesAsync(
        string localNodeId, CancellationToken ct = default)
    {
        var nodes = await db.Nodes
            .AsNoTracking()
            .Where(n => n.NodeId != localNodeId
                && n.LifecycleState == NodeLifecycleState.Active
                && !n.MaintenanceMode)
            .OrderBy(n => n.NodeId)
            .Select(n => new SourceNodeInfo(n.NodeId, n.SyncUrl))
            .ToListAsync(ct);

        return nodes.AsReadOnly();
    }

    public Task<bool> IsHubAsync(string nodeId, CancellationToken ct = default) =>
        db.Nodes.AnyAsync(n => n.UpstreamNodeId == nodeId, ct);
}
