namespace MSOSync.Metadata.BatchErrors;

public sealed record BatchErrorSummaryDto(
    long     ErrorId,
    long     BatchId,
    long?    EventId,
    string?  ConflictType,
    string   Severity,
    string?  ErrorMessage,
    DateTime CreateTime,
    int      RetryCount);
