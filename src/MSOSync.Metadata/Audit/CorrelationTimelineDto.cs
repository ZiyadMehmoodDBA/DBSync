namespace MSOSync.Metadata.Audit;

public sealed record CorrelationTimelineDto(
    string CorrelationId,
    Guid? OperationId,
    string? OperationType,
    string? OperationStatus,
    string? OperationResult,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    TimeSpan? Duration,
    string? InitiatedBy,
    EntityChipDto[] EntityChips,
    int TotalEventCount,
    bool IsFailedWorkflow,
    string? FailureSummary,
    CorrelationPhaseDto[] Phases);

public sealed record CorrelationPhaseDto(
    string PhaseName,
    string Category,
    CorrelationEventDto[] Events,
    bool HasErrors);

public sealed record CorrelationEventDto(
    long AuditId,
    DateTime OccurredAt,
    TimeSpan? DurationSincePrevious,
    string ActionName,
    string Summary,
    string? ActorUsername,
    string Category,
    string Severity,
    string? EntityType,
    string? EntityId,
    string? DeepLink);

public sealed record EntityChipDto(
    string EntityType,
    string EntityId,
    string? DisplayLabel);

public sealed record CorrelationSearchResultDto(
    string CorrelationId,
    int EventCount,
    DateTime FirstSeen,
    DateTime LastSeen,
    string? PrimaryEntityType,
    bool IsFailedWorkflow);
