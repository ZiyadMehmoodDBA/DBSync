namespace MSOSync.Metadata.Events;

public sealed record EventDetailDto(
    long     EventId,
    string   TriggerId,
    string   SourceNodeId,
    string   ChannelId,
    char     EventType,
    string   TableName,
    string?  PkData,
    string?  RowData,
    long?    TransactionId,
    long?    BatchId,
    DateTime CreateTime,
    bool     IsProcessed);
