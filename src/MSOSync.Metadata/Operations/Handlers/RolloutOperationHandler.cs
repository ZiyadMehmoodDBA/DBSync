using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations.Handlers;

/// <summary>
/// Delegates Rollout operation cancel to the sync_configuration_rollout table.
/// The referenceId is the SyncConfigurationRollout.Id (rollout_id).
/// </summary>
public sealed class RolloutOperationHandler(AppDbContext db) : IOperationHandler
{
    public OperationType OperationType => OperationType.Rollout;

    public async Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct)
    {
        // Mark the rollout row as Cancelled. The background fire-and-forget loop
        // in RolloutService checks Status and aborts when it sees a non-InProgress value.
        var updated = await db.ConfigurationRollouts
            .Where(r => r.Id == referenceId && r.Status == "InProgress")
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.Status,      "Cancelled")
                 .SetProperty(r => r.CompletedAt, DateTime.UtcNow),
                ct);

        if (updated == 0)
        {
            // Either not found or already in a terminal state — treat as idempotent.
            // Do not throw: the operation row will still be marked Cancelled by OperationService.
        }
    }

    public Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct)
    {
        // Rollout retry is not safe to implement generically without knowing which
        // nodes still need to be addressed. The operator should create a new rollout.
        throw new NotSupportedException(
            "Rollout retry is not supported. Create a new rollout targeting the failed nodes.");
    }
}
