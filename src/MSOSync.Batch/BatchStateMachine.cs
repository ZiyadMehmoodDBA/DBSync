using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class BatchStateMachine(AppDbContext db) : IBatchStateMachine
{
    private static readonly HashSet<(BatchStatus From, BatchStatus To)> ValidTransitions =
    [
        (BatchStatus.New,   BatchStatus.Sent),
        (BatchStatus.New,   BatchStatus.Retry),
        (BatchStatus.Sent,  BatchStatus.Ok),
        (BatchStatus.Sent,  BatchStatus.Error),
        (BatchStatus.Error, BatchStatus.Retry),
        (BatchStatus.Retry, BatchStatus.Sent),
        (BatchStatus.Retry, BatchStatus.Error),
    ];

    public bool CanTransition(BatchStatus from, BatchStatus to) =>
        ValidTransitions.Contains((from, to));

    public async Task<bool> TransitionAsync(
        long batchId, BatchStatus from, BatchStatus to, CancellationToken ct = default)
    {
        if (!CanTransition(from, to)) return false;

        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId && b.Status == (byte)from)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, (byte)to), ct);

        return rows == 1;
    }
}
