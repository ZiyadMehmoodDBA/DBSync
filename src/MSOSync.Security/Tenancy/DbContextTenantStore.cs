using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Security.Tenancy;

public sealed class DbContextTenantStore(AppDbContext db) : ITenantStore
{
    public Task<Tenant?> FindTenantAsync(Guid tenantId, CancellationToken ct)
        => db.Tenants
             .AsNoTracking()
             .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

    public Task<TenantMembership?> FindMembershipAsync(Guid tenantId, long userId, CancellationToken ct)
        => db.TenantMemberships
             .AsNoTracking()
             .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);
}
