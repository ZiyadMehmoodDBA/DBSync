namespace MSOSync.Persistence.Entities;

public sealed class SyncRuntimeStats
{
    public long StatId { get; set; }
    public long? HeapUsed { get; set; }
    public long? HeapMax { get; set; }
    public int? ThreadCount { get; set; }
    public decimal? CpuPercent { get; set; }
    public long? GcCount { get; set; }
    public long? GcTimeMs { get; set; }
    public long? UptimeMs { get; set; }
    public DateTime? CreateTime { get; set; }
}
