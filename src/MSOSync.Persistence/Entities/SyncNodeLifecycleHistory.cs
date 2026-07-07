namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeLifecycleHistory
{
    public long HistoryId { get; set; }
    public string NodeId { get; set; } = null!;
    public NodeLifecycleState? FromState { get; set; }   // null = entry into canonical model
    public NodeLifecycleState ToState { get; set; }
    public LifecycleTrigger Trigger { get; set; }
    public string? Reason { get; set; }
    public string Actor { get; set; } = null!;            // username or "system"
    public Guid? CorrelationId { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
