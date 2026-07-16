using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[HybridEntity]
public sealed class SyncRole : IHybridEntity
{
    public long RoleId { get; set; }
    public string RoleName { get; set; } = null!;
    public Guid? TenantId { get; set; }  // null = system role; non-null = tenant custom role
}
