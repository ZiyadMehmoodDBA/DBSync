namespace MSOSync.Metadata.Operations.Rolling;

public sealed record RollingStepDto(
    Guid      StepId,
    string    NodeId,
    int       WaveNumber,
    string    Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string?   ErrorMessage);

public sealed record RollingOperationDetailDto(
    Guid                        OperationId,
    string                      OperationType,
    string                      Status,
    string?                     Result,
    RollingOperationPolicy      Policy,
    IReadOnlyList<RollingStepDto> Steps);
