namespace MSOSync.Metadata.Operations.Timeline.Dtos;

public sealed record OperationTimelineDto(
    IReadOnlyList<OperationTimelineItemDto> Items,
    DateTime  From,
    DateTime  To,
    bool      HasMore,
    int       ReturnedCount);

public sealed record OperationTimelineItemDto(
    Guid      OperationId,
    string    Type,
    string    Status,
    string?   Label,
    DateTime  StartedAt,
    DateTime? CompletedAt,
    int?      ProgressPercent);
