namespace MSOSync.Persistence.Entities;

public sealed class SyncConfigurationRollout
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "Queued";             // Queued / InProgress / Completed / Failed / Cancelled
    public Guid TemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public int TargetNodeCount { get; set; }
    public int AppliedCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCount { get; set; }
    public int ProgressPercent { get; set; }
    public Guid InitiatedBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
