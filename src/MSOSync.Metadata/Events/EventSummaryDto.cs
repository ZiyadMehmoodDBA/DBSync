namespace MSOSync.Metadata.Events;

public sealed record EventSummaryDto(
    long     EventId,
    string   TriggerId,
    string   SourceNodeId,
    string   ChannelId,
    char     EventType,
    string   TableName,
    long?    BatchId,
    DateTime CreateTime,
    bool     IsProcessed);
