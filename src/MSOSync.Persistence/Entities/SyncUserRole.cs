using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[HybridEntity]
public sealed class SyncUserRole : IHybridEntity
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public Guid? TenantId { get; set; }  // null = system role; non-null = tenant custom role
}
