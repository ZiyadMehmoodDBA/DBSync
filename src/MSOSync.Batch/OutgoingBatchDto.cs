namespace MSOSync.Batch;

public sealed record OutgoingBatchDto(
    long BatchId,
    BatchStatus Status,
    string TargetNodeId,
    string ChannelId,
    DateTime? CreateTime,
    DateTime? SentTime,
    DateTime? AckTime,
    int RetryCount,
    int EventCount,
    string? ErrorMessage);
