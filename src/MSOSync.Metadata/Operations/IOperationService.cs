namespace MSOSync.Metadata.Operations;

public interface IOperationService
{
    /// <summary>
    /// Persists a new sync_operation row in Pending status and returns its ID.
    /// Call this at the START of a long-running job before returning to the caller.
    /// </summary>
    Task<Guid> CreateAsync(
        OperationType   type,
        Guid?           referenceId,
        Guid?           initiatedBy,
        OperationSource source,
        string          correlationId,
        bool            canCancel,
        bool            canRetry,
        string          summary,
        string?         metadataJson,
        CancellationToken ct);

    /// <summary>Updates progress_percent and progress_message. Status stays Running.</summary>
    Task UpdateProgressAsync(Guid operationId, int percent, string? message, CancellationToken ct);

    /// <summary>
    /// Marks the operation Completed and sets result + completed_at.
    /// Pass a new summary if the final summary differs from the initial one.
    /// </summary>
    Task CompleteAsync(Guid operationId, OperationResult result, string? summary, CancellationToken ct);

    /// <summary>
    /// Cancels a Pending or Running operation. Delegates to the domain handler
    /// for domain-side cancellation logic, then marks the row Cancelled.
    /// Throws InvalidOperationException if the operation's current status does not
    /// allow cancellation (i.e. it is already terminal).
    /// </summary>
    Task CancelAsync(Guid operationId, Guid actorId, CancellationToken ct);

    /// <summary>
    /// Retries a Failed or Cancelled operation by resetting it to Pending and
    /// delegating to the domain handler to re-enqueue the work.
    /// Throws InvalidOperationException if can_retry = false or status is not retryable.
    /// </summary>
    Task RetryAsync(Guid operationId, Guid actorId, CancellationToken ct);
}
