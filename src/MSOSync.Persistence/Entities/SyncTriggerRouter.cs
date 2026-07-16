using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncTriggerRouter : ITenantScoped
{
    public string TriggerId { get; set; } = null!;
    public string RouterId { get; set; } = null!;
    public bool Enabled { get; set; } = true;

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
