namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record ReplayItemDto(
    Guid    ItemId,
    string  NodeId,
    string  ChannelId,
    int     EventCount,
    string  Status,
    string? ErrorMessage,
    long?   SourceBatchId,
    long?   ReplayBatchId);
