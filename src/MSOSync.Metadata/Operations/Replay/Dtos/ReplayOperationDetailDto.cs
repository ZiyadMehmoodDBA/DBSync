namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record ReplayOperationDetailDto(
    Guid      OperationId,
    string    Status,
    string?   Result,
    string    NodeId,
    string    ReplayMode,
    DateTime  FromTime,
    DateTime  ToTime,
    string[]? ChannelIds,
    long[]?   BatchIds,
    int       TotalItems,
    int       CompletedItems,
    int       FailedItems,
    int       SkippedItems,
    DateTime? StartedAt,
    DateTime? CompletedAt);
