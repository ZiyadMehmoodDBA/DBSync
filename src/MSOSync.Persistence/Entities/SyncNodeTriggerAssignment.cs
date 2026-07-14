namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeTriggerAssignment
{
    public string NodeId { get; set; } = null!;
    public string TriggerId { get; set; } = null!;
}
