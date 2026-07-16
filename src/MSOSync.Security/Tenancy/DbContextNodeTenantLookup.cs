using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Security.Tenancy;

public sealed class DbContextNodeTenantLookup(AppDbContext db) : INodeTenantLookup
{
    public async Task<Guid?> GetNodeTenantIdAsync(string nodeId, CancellationToken ct)
    {
        // IgnoreQueryFilters because the node's TenantId column is not yet populated
        // (it's added in Task 7). After Task 7 this query works normally.
        var node = await db.Nodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.NodeId == nodeId)
            .Select(n => new { n.TenantId })
            .FirstOrDefaultAsync(ct);

        return node?.TenantId;
    }
}
