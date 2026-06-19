using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Queries;

public sealed class GetNodeSecurityQuery(AppDbContext db)
{
    public Task<SyncNodeSecurity?> ExecuteAsync(string nodeId, CancellationToken ct = default)
        => db.NodeSecurities
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);
}
