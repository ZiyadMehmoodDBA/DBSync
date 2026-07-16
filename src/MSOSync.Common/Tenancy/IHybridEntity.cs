namespace MSOSync.Common.Tenancy;

// Marker for Hybrid entities: nullable TenantId, no EF global filter.
// Use IHybridLookupService for tenant-aware queries.
public interface IHybridEntity { }
