namespace MSOSync.Metadata.Operations.Rolling;

public interface IRollingOperationService
{
    Task<Guid> CreateAsync(OperationType kind, IReadOnlyList<string> nodeIds,
        RollingOperationPolicy policy, Guid? initiatedBy, string actor, CancellationToken ct = default);
    Task PauseAsync(Guid operationId, CancellationToken ct = default);
    Task ResumeAsync(Guid operationId, CancellationToken ct = default);
    Task AbortAsync(Guid operationId, string actor, CancellationToken ct = default);
    Task ConfirmStepAsync(Guid stepId, CancellationToken ct = default);
}
