using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetNodeByIdQuery(AppDbContext db)
{
    public Task<SyncNode?> ExecuteAsync(string nodeId, CancellationToken ct = default)
        => db.Nodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}
