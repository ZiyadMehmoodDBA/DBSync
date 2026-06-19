namespace MSOSync.Persistence.Entities;

public sealed class SyncOutgoingBatch
{
    public long BatchId { get; set; }
    public long BatchSequence { get; set; }
    public string NodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public byte Status { get; set; }
    public int RowCount { get; set; } = 0;
    public long ByteCount { get; set; } = 0L;
    public int RetryCount { get; set; } = 0;
    public DateTime? NextRetryTime { get; set; }
    public long? NetworkMillis { get; set; }
    public DateTime? CreateTime { get; set; }
    public DateTime? SentTime { get; set; }
    public DateTime? AckTime { get; set; }
}
