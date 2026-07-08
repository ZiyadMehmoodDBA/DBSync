namespace MSOSync.Persistence.Entities;

public sealed class SyncNodeConfigurationHistory
{
    public Guid Id { get; set; }
    public string NodeId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    // EventType values: Assigned / Unassigned / Applied / ApplyFailed /
    //                   RolledBack / DriftDetected / DriftCleared / PublishDetected
    public Guid? TemplateId { get; set; }
    public int? TemplateVersion { get; set; }
    public string? ConfigurationHash { get; set; }
    public string? CorrelationId { get; set; }                  // groups rollout events
    public Guid? ActorId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Notes { get; set; }
}
