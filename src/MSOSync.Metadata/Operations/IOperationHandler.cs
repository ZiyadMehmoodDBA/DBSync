namespace MSOSync.Metadata.Operations;

/// <summary>
/// Strategy interface implemented by each domain service that can own an operation.
/// Registered as keyed-scoped by OperationType.
/// </summary>
public interface IOperationHandler
{
    OperationType OperationType { get; }

    /// <summary>
    /// Performs domain-level cancellation (e.g. marks rollout as cancelled in DB).
    /// Called by OperationService.CancelAsync BEFORE the operation row is updated.
    /// </summary>
    Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct);

    /// <summary>
    /// Re-enqueues or re-starts the domain work for a retry.
    /// Called by OperationService.RetryAsync BEFORE the operation row is reset to Pending.
    /// </summary>
    Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct);
}
