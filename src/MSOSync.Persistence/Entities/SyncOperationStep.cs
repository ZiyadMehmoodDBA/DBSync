using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncOperationStep : ITenantScoped
{
    public Guid   StepId       { get; set; }
    public Guid   OperationId  { get; set; }               // FK -> sync_operation
    public string NodeId       { get; set; } = null!;
    public int    WaveNumber   { get; set; }               // 1-based
    public string Status       { get; set; } = null!;      // RollingStepStatus as string
    public DateTime? StartedAt   { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage  { get; set; }
    public Guid   TenantId     { get; set; }
}
