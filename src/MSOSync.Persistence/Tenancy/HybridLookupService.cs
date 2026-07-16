using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Tenancy;

public sealed class HybridLookupService(AppDbContext db) : IHybridLookupService
{
    public async Task<SyncParameter?> GetParameterAsync(Guid tenantId, string paramName, CancellationToken ct)
    {
        // Try tenant-specific first
        var tenantRecord = await db.Parameters
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ParameterName == paramName, ct);

        if (tenantRecord is not null)
            return tenantRecord;

        // Fall back to platform default (NULL TenantId)
        return await db.Parameters
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == null && p.ParameterName == paramName, ct);
    }

    public async Task<IReadOnlyList<SyncParameter>> GetAllParametersAsync(Guid tenantId, CancellationToken ct)
    {
        // Merge: start with all platform defaults, override with tenant-specific values
        var platform = await db.Parameters
            .AsNoTracking()
            .Where(p => p.TenantId == null)
            .ToListAsync(ct);

        var tenantSpecific = await db.Parameters
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(ct);

        var tenantNames = tenantSpecific.Select(p => p.ParameterName).ToHashSet();
        var merged      = tenantSpecific.Concat(platform.Where(p => !tenantNames.Contains(p.ParameterName)));
        return merged.ToList();
    }

    public async Task<bool> ParameterExistsAsync(Guid tenantId, string paramName, CancellationToken ct)
        => await GetParameterAsync(tenantId, paramName, ct) is not null;
}
