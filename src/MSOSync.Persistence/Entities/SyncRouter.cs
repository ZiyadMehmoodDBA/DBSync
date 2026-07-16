using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncRouter : ITenantScoped
{
    public string RouterId { get; set; } = null!;
    public string SourceNodeGroup { get; set; } = null!;
    public string TargetNodeGroup { get; set; } = null!;
    public string RouterType { get; set; } = "default";
    public bool Enabled { get; set; } = true;

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
