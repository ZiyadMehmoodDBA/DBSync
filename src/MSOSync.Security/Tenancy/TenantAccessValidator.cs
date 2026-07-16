using MSOSync.Persistence.Entities;

namespace MSOSync.Security.Tenancy;

public sealed class TenantAccessValidator(ITenantStore store) : ITenantAccessValidator
{
    public async Task<TenantValidationResult> ValidateAsync(Guid tenantId, long userId, CancellationToken ct)
    {
        var membership = await store.FindMembershipAsync(tenantId, userId, ct);
        if (membership is null)
            throw new TenantAccessException(403, "Tenant membership not found");

        if (membership.Status != MemberStatus.Active)
            throw new TenantAccessException(403, $"Tenant membership status is {membership.Status}");

        var tenant = await store.FindTenantAsync(tenantId, ct);
        if (tenant is null)
            throw new TenantAccessException(403, "Tenant not found");

        if (tenant.Status != TenantStatus.Active)
            throw new TenantAccessException(409, $"Tenant is {tenant.Status.ToString().ToLower()}");

        return new TenantValidationResult(tenant.TenantId, tenant.Slug, tenant.Edition, membership.RoleId);
    }
}
