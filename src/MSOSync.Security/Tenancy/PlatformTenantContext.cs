using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class PlatformTenantContext : ITenantContext
{
    public static readonly PlatformTenantContext Instance = new();

    public Guid        TenantId          => Guid.Empty;
    public string      TenantSlug        => "";
    public EditionType Edition           => EditionType.Enterprise;   // platform has no edition restriction
    public long?       UserId            => null;
    public long?       RoleId            => null;
    public bool        IsPlatformContext => true;

    private PlatformTenantContext() { }
}
