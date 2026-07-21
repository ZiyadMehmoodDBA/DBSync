namespace MSOSync.Security;

public sealed record SwitchTenantContext(string Username, string TenantSlug, IReadOnlyList<string> Roles);

public interface ITenantMembershipQueryService
{
    Task<SwitchTenantContext?> GetSwitchTenantContextAsync(long userId, Guid tenantId, CancellationToken ct = default);
}
