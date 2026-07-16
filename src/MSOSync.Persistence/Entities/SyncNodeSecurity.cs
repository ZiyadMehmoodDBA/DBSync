using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeSecurity : ITenantScoped
{
    public string NodeId { get; set; } = null!;
    public string CurrentTokenHash { get; set; } = null!;
    public string? NextTokenHash { get; set; }
    public DateTime? RotationScheduled { get; set; }
    public DateTime? CreatedTime { get; set; }

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
