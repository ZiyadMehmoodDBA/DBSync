namespace MSOSync.Common.Tenancy;

// Implemented by every Tenant Scoped entity that HAS a TenantId column.
// EF Core's ApplyTenantFilters() auto-registers a global query filter for all implementors.
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
