using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid        TenantId          { get; }
    public string      TenantSlug        { get; }
    public EditionType Edition           { get; }
    public long?       UserId            { get; }
    public long?       RoleId            { get; }
    public bool        IsPlatformContext => false;

    public TenantContext(
        Guid        tenantId,
        string      tenantSlug,
        EditionType edition,
        long?       userId,
        long?       roleId)
    {
        TenantId   = tenantId;
        TenantSlug = tenantSlug;
        Edition    = edition;
        UserId     = userId;
        RoleId     = roleId;
    }
}
