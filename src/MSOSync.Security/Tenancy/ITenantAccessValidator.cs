using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed record TenantValidationResult(
    Guid        TenantId,
    string      TenantSlug,
    EditionType Edition,
    long        RoleId);

public interface ITenantAccessValidator
{
    // Throws TenantAccessException (403/409) on any violation.
    Task<TenantValidationResult> ValidateAsync(Guid tenantId, long userId, CancellationToken ct);
}
