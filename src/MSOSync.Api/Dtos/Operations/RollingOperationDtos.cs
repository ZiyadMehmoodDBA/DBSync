namespace MSOSync.Api.Dtos.Operations;

public sealed record CreateRollingOperationRequest(
    string                Kind,
    IReadOnlyList<string> NodeIds,
    int?                  WaveSize,
    int?                  WavePercent,
    int                   GateSoakSeconds,
    string                WaveAction,
    int?                  WindowSeconds,
    string?               TargetVersion,
    int                   VerificationTimeoutSeconds);

public sealed record CreateRollingOperationResponse(Guid OperationId);
