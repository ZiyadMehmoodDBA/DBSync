using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed class NodeReadQueryService(AppDbContext db) : INodeReadQueryService
{
    public Task<SyncNode?> GetNodeAsync(string nodeId, CancellationToken ct = default)
        => db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}
