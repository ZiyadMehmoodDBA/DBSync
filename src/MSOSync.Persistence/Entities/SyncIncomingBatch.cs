namespace MSOSync.Persistence.Entities;

public sealed class SyncIncomingBatch
{
    public long BatchId { get; set; }
    public string NodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public IncomingBatchStatus Status { get; set; } = IncomingBatchStatus.New;
    public int? RowCount { get; set; }
    public long BatchSequence { get; set; }
    public string SourceNodeId { get; set; } = null!;
    public DateTime ReceivedTime { get; set; }
    public DateTime? LoadTime { get; set; }
    public DateTime? ExtractTime { get; set; }
    public DateTime? AppliedTime { get; set; }
    public long? ApplyTimeMs { get; set; }
}
