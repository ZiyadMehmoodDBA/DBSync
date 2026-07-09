namespace MSOSync.Persistence.Entities;

public sealed class SyncOperation
{
    public Guid   OperationId      { get; set; }
    public string OperationType    { get; set; } = null!;   // Export|Rollout|Decommission|Recovery
    public Guid?  ReferenceId      { get; set; }            // FK to the domain object (job_id / rollout_id / node_id)
    public string Status           { get; set; } = null!;   // Pending|Running|Completed|Failed|Cancelled
    public string? Result          { get; set; }            // Success|PartialSuccess|Failure|Cancelled
    public string Source           { get; set; } = null!;   // User|System|Scheduler|Worker|Api
    public int?   ProgressPercent  { get; set; }
    public string? ProgressMessage { get; set; }
    public string? CorrelationId   { get; set; }
    public Guid?  InitiatedBy      { get; set; }
    public string? MetadataJson    { get; set; }
    public string? Summary         { get; set; }
    public bool   CanCancel        { get; set; }
    public bool   CanRetry         { get; set; }
    public DateTime  StartedAt     { get; set; }
    public DateTime? CompletedAt   { get; set; }
}
