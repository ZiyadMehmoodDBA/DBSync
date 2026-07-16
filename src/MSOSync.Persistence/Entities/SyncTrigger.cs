using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public sealed class SyncTrigger : ITenantScoped
{
    public string TriggerId { get; set; } = null!;
    public string SourceTable { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public bool SyncOnInsert { get; set; } = true;
    public bool SyncOnUpdate { get; set; } = true;
    public bool SyncOnDelete { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public int TriggerVersion { get; set; } = 0;
    public DateTime? LastVerifiedTime { get; set; }
    public string? PkColumnsJson { get; set; }

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
