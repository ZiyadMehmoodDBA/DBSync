namespace MSOSync.Persistence.Entities;

public sealed class SyncTrigger
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
}
