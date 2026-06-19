using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetOfflineNodesQuery(AppDbContext db)
{
    public Task<List<SyncNode>> ExecuteAsync(
        int thresholdMinutes, CancellationToken ct = default)
        => db.Nodes
            .AsNoTracking()
            .Where(n => n.LastHeartbeat < DateTime.UtcNow.AddMinutes(-thresholdMinutes)
                     && n.Status == "REGISTERED")
            .ToListAsync(ct);
}
