using Microsoft.EntityFrameworkCore;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class BatchStateMachine(AppDbContext db, IClock clock) : IBatchStateMachine
{
    // Valid (from, to) transitions
    private static readonly HashSet<(BatchStatus From, BatchStatus To)> ValidTransitions =
    [
        (BatchStatus.New,          BatchStatus.Sending),      // PUSH: start sending
        (BatchStatus.New,          BatchStatus.Acknowledged),  // PULL: ACK success (batch was never moved)
        (BatchStatus.New,          BatchStatus.Error),         // PULL: negative ACK
        (BatchStatus.New,          BatchStatus.Retry),         // Recovery: stale New → Retry
        (BatchStatus.Sending,      BatchStatus.Acknowledged),  // PUSH: success
        (BatchStatus.Sending,      BatchStatus.Error),         // PUSH: failure / timeout
        (BatchStatus.Error,        BatchStatus.Retry),
        (BatchStatus.Retry,        BatchStatus.Sending),       // PUSH retry
        (BatchStatus.Retry,        BatchStatus.Acknowledged),  // PULL retry → ack
        (BatchStatus.Retry,        BatchStatus.Error),
    ];

    public async Task<bool> MoveToSendingAsync(long batchId, CancellationToken ct = default)
    {
        var sentTime = clock.UtcNow;

        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId
                     && (b.Status == (byte)BatchStatus.New || b.Status == (byte)BatchStatus.Retry))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status,   (byte)BatchStatus.Sending)
                .SetProperty(b => b.SentTime, sentTime), ct);

        return rows == 1;
    }

    public async Task<bool> MoveToAcknowledgedAsync(
        long batchId, DateTimeOffset ackTime, CancellationToken ct = default)
    {
        var ackUtc = ackTime.UtcDateTime;

        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId
                     && (b.Status == (byte)BatchStatus.New
                      || b.Status == (byte)BatchStatus.Sending
                      || b.Status == (byte)BatchStatus.Retry))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status,  (byte)BatchStatus.Acknowledged)
                .SetProperty(b => b.AckTime, ackUtc), ct);

        return rows == 1;
    }

    public async Task<bool> MoveToErrorAsync(long batchId, CancellationToken ct = default)
    {
        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId
                     && (b.Status == (byte)BatchStatus.New
                      || b.Status == (byte)BatchStatus.Sending
                      || b.Status == (byte)BatchStatus.Retry))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, (byte)BatchStatus.Error), ct);

        return rows == 1;
    }

    public async Task<bool> MoveToRetryAsync(long batchId, CancellationToken ct = default)
    {
        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId
                     && (b.Status == (byte)BatchStatus.New || b.Status == (byte)BatchStatus.Error))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, (byte)BatchStatus.Retry), ct);

        return rows == 1;
    }
}
