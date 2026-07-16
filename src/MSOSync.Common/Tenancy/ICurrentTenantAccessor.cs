namespace MSOSync.Common.Tenancy;

// Singleton — reads current request's ITenantContext via IHttpContextAccessor.
// Used by AppDbContext query filters to avoid EF model-cache issues.
public interface ICurrentTenantAccessor
{
    Guid? TenantId { get; }   // null = platform context or no active request
}
