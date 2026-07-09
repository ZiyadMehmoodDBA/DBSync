namespace MSOSync.Metadata.Operations;

public sealed record OperationDto(
    Guid            OperationId,
    string          OperationType,
    Guid?           ReferenceId,
    string          Status,
    string?         Result,
    string          Source,
    int?            ProgressPercent,
    string?         ProgressMessage,
    string?         CorrelationId,
    Guid?           InitiatedBy,
    string?         MetadataJson,
    string?         Summary,
    bool            CanCancel,
    bool            CanRetry,
    DateTime        StartedAt,
    DateTime?       CompletedAt,
    int?            QueuePosition   // non-null only for Pending operations
);

public sealed record OperationPageDto(
    IReadOnlyList<OperationDto> Items,
    string?                     NextCursor,
    int?                        TotalCount);

public sealed record OperationDetailDto(
    Guid            OperationId,
    string          OperationType,
    Guid?           ReferenceId,
    string          Status,
    string?         Result,
    string          Source,
    int?            ProgressPercent,
    string?         ProgressMessage,
    string?         CorrelationId,
    Guid?           InitiatedBy,
    string?         MetadataJson,
    string?         Summary,
    bool            CanCancel,
    bool            CanRetry,
    DateTime        StartedAt,
    DateTime?       CompletedAt);

public sealed record OperationFilter(
    string[]?  Types      = null,
    string[]?  Statuses   = null,
    string[]?  Sources    = null,
    DateTime?  From       = null,
    DateTime?  To         = null,
    string?    InitiatedBy = null,
    string?    Cursor     = null,
    int        PageSize   = 25);
