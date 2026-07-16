using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class DbContextNodeTenantLookup(IPlatformRepository<SyncNode> nodeRepo) : INodeTenantLookup
{
    public async Task<Guid?> GetNodeTenantIdAsync(string nodeId, CancellationToken ct)
    {
        var node = await nodeRepo.QueryAll()
            .Where(n => n.NodeId == nodeId)
            .Select(n => new { n.TenantId })
            .FirstOrDefaultAsync(ct);

        return node?.TenantId;
    }
}
