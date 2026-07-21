namespace MSOSync.Api.Dtos.Export;

public sealed record ExportJobDto(
    Guid            JobId,
    Guid?           ParentJobId,
    string          RequestedBy,
    string          ResourceType,
    string          Format,
    string          Status,
    int             ProgressPercent,
    long?           RowCount,
    string?         ErrorMessage,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
