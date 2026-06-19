namespace MSOSync.Persistence.Entities;

public sealed class SyncDataEvent
{
    public long EventId { get; set; }
    public string TriggerId { get; set; } = null!;
    public string SourceNodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public char EventType { get; set; }
    public string TableName { get; set; } = null!;
    public string? PkData { get; set; }
    public string? RowData { get; set; }
    public long? TransactionId { get; set; }
    public DateTime CreateTime { get; set; }
    public bool IsProcessed { get; set; } = false;
}
