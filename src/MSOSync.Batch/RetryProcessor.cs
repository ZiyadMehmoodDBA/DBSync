using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class RetryProcessor(
    AppDbContext db,
    IBatchStateMachine stateMachine,
    IClock clock,
    ILogger<RetryProcessor> logger)
{
    private const int MaxRetries = 5;

    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var candidates = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Error
                     && b.RetryCount < MaxRetries
                     && (b.NextRetryTime == null || b.NextRetryTime <= now))
            .ToListAsync(ct);

        var count = 0;
        foreach (var batch in candidates)
        {
            var succeeded = await stateMachine.TransitionAsync(
                batch.BatchId, BatchStatus.Error, BatchStatus.Retry, ct);

            if (!succeeded) continue;

            var delayMinutes = Math.Pow(2, batch.RetryCount) * 5.0; // RetryCount is pre-increment
            batch.NextRetryTime = now.AddMinutes(delayMinutes);
            batch.RetryCount++;
            await db.SaveChangesAsync(ct);
            count++;

            logger.LogInformation("Batch {BatchId} queued for retry #{Count}, next={Next:u}",
                batch.BatchId, batch.RetryCount, batch.NextRetryTime);
        }

        return count;
    }
}
