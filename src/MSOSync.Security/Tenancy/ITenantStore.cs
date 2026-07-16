using MSOSync.Persistence.Entities;

namespace MSOSync.Security.Tenancy;

public interface ITenantStore
{
    Task<Tenant?>           FindTenantAsync    (Guid tenantId, CancellationToken ct);
    Task<TenantMembership?> FindMembershipAsync(Guid tenantId, long userId, CancellationToken ct);
}
