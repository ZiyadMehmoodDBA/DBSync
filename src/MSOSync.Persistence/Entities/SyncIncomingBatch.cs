namespace MSOSync.Persistence.Entities;

public sealed class SyncIncomingBatch
{
    public long BatchId { get; set; }
    public string NodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public byte Status { get; set; }
    public int? RowCount { get; set; }
    public DateTime? LoadTime { get; set; }
    public DateTime? ExtractTime { get; set; }
    public DateTime? AppliedTime { get; set; }
    public long? ApplyTimeMs { get; set; }
}
