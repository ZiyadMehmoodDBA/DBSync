namespace MSOSync.Persistence.Entities;

public sealed class SyncTriggerRouter
{
    public string TriggerId { get; set; } = null!;
    public string RouterId { get; set; } = null!;
    public bool Enabled { get; set; } = true;
}
