namespace MSOSync.Persistence.Entities;

public sealed class SyncTriggerHist
{
    public long HistId { get; set; }
    public string TriggerId { get; set; } = null!;
    public string? DdlText { get; set; }
    public int? TriggerVersion { get; set; }
    public DateTime? CreateTime { get; set; }
}
