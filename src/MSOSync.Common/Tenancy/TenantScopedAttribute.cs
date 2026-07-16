namespace MSOSync.Common.Tenancy;

// Applied to Tenant Scoped entities whose TenantId column migration is deferred
// to a future epic. Once migrated, the entity implements ITenantScoped instead.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantScopedAttribute : Attribute { }
