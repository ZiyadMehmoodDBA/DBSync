using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncTriggerHist : ITenantScoped
{
    public long HistId { get; set; }
    public string TriggerId { get; set; } = null!;
    public string? DdlText { get; set; }
    public int? TriggerVersion { get; set; }
    public DateTime? CreateTime { get; set; }

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
