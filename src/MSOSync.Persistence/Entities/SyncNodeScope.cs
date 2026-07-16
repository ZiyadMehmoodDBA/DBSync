using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeScope : ITenantScoped
{
    public string NodeId { get; set; } = null!;
    public SyncDirection SyncDirection { get; set; } = SyncDirection.Bidirectional;
    public InitialLoadPolicy InitialLoadPolicy { get; set; } = InitialLoadPolicy.None;
    public DateTime CreatedTime { get; set; }
    public DateTime UpdatedTime { get; set; }

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
