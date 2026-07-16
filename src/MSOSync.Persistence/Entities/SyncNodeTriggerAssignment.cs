using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeTriggerAssignment : ITenantScoped
{
    public string NodeId { get; set; } = null!;
    public string TriggerId { get; set; } = null!;

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
