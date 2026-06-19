namespace MSOSync.Persistence.Entities;

public sealed class SyncMonitor
{
    public long SnapshotId { get; set; }
    public string? NodeId { get; set; }
    public string? MetricName { get; set; }
    public string? MetricValue { get; set; }
    public DateTime? CreateTime { get; set; }
}
