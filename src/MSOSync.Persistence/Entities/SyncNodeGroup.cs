using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeGroup : ITenantScoped
{
    public string GroupId { get; set; } = null!;
    public string? GroupName { get; set; }

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
