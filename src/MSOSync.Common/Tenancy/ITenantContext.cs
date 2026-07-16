namespace MSOSync.Common.Tenancy;

public interface ITenantContext
{
    Guid        TenantId          { get; }
    string      TenantSlug        { get; }
    EditionType Edition           { get; }
    long?       UserId            { get; }   // null for node tokens and platform tokens
    long?       RoleId            { get; }   // from TenantMembership.RoleId; null for platform
    bool        IsPlatformContext { get; }   // true → TenantId == Guid.Empty, RoleId == null
}
