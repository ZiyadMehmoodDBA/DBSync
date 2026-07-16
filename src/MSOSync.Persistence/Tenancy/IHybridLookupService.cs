using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Tenancy;

public interface IHybridLookupService
{
    // Returns tenant-specific SyncParameter if exists, else platform (NULL TenantId) record.
    Task<SyncParameter?>               GetParameterAsync   (Guid tenantId, string paramName, CancellationToken ct);
    Task<IReadOnlyList<SyncParameter>> GetAllParametersAsync(Guid tenantId, CancellationToken ct);
    Task<bool>                         ParameterExistsAsync (Guid tenantId, string paramName, CancellationToken ct);
}
