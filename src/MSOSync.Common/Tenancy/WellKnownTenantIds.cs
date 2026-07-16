namespace MSOSync.Common.Tenancy;

public static class WellKnownTenantIds
{
    // Fixed GUID for the Community Edition SystemTenant — used in migrations for backfill DEFAULT.
    public static readonly Guid SystemTenant = new("00000000-0000-0000-0000-000000000001");
}
