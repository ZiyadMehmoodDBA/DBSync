using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

/// <summary>
/// Handles both outgoing ACK (PUSH mode — source side) and incoming ACK (PULL mode — from POST /ack).
/// </summary>
public sealed class AcknowledgementService(
    IBatchStateMachine              stateMachine,
    AppDbContext                    db,
    ILogger<AcknowledgementService> logger)
{
    /// <summary>
    /// Called by SmartTransportService after a PUSH attempt completes.
    /// </summary>
    public async Task AcknowledgeOutgoingAsync(
        long              batchId,
        bool              success,
        DateTimeOffset    ackTime,
        string?           errorMessage,
        CancellationToken ct = default)
    {
        if (success)
        {
            await stateMachine.MoveToAcknowledgedAsync(batchId, ackTime, ct);
            logger.LogInformation("Batch {BatchId} acknowledged at {AckTime}", batchId, ackTime);
        }
        else
        {
            await stateMachine.MoveToErrorAsync(batchId, ct);
            if (errorMessage != null)
            {
                db.BatchErrors.Add(new SyncBatchError
                {
                    BatchId      = batchId,
                    ConflictType = TransportFailureReason.HttpError.ToString(),
                    ErrorMessage = errorMessage
                });
                await db.SaveChangesAsync(ct);
            }
            logger.LogWarning("Batch {BatchId} push failed: {Error}", batchId, errorMessage);
        }
    }

    /// <summary>
    /// Called by SyncController POST /ack — handles ACK from a PULL target.
    /// Returns false if batch not found.
    /// Idempotent: already-Acknowledged batch returns true (no-op).
    /// </summary>
    public async Task<bool> AcknowledgeIncomingAsync(
        AckPayload        payload,
        CancellationToken ct = default)
    {
        var batch = await db.OutgoingBatches.FindAsync([payload.BatchId], ct);
        if (batch == null) return false;

        // Idempotent: already acknowledged.
        if (batch.Status == (byte)BatchStatus.Acknowledged)
        {
            logger.LogDebug("Batch {BatchId} already acknowledged — ignoring duplicate ACK",
                payload.BatchId);
            return true;
        }

        if (payload.Success)
        {
            await stateMachine.MoveToAcknowledgedAsync(payload.BatchId, payload.AckTime, ct);
        }
        else
        {
            await stateMachine.MoveToErrorAsync(payload.BatchId, ct);
            db.BatchErrors.Add(new SyncBatchError
            {
                BatchId      = payload.BatchId,
                ConflictType = payload.ErrorCode?.StartsWith("SEQUENCE_GAP", StringComparison.Ordinal) == true
                    ? "SequenceGap"
                    : TransportFailureReason.ApplyFailure.ToString(),
                ErrorMessage = payload.ErrorCode
            });
            await db.SaveChangesAsync(ct);
        }

        return true;
    }
}
