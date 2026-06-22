using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class RetryProcessor(
    AppDbContext db,
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
            var delayMinutes = Math.Pow(2, batch.RetryCount) * 5.0; // RetryCount is pre-increment
            var newRetryTime = now.AddMinutes(delayMinutes);
            var newRetryCount = batch.RetryCount + 1;

            var rows = await db.OutgoingBatches
                .Where(b => b.BatchId == batch.BatchId && b.Status == (byte)BatchStatus.Error)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status,        (byte)BatchStatus.Retry)
                    .SetProperty(b => b.RetryCount,    newRetryCount)
                    .SetProperty(b => b.NextRetryTime, newRetryTime), ct);

            if (rows != 1) continue; // lost race — skip

            count++;
            logger.LogInformation("Batch {BatchId} queued for retry #{Count}, next={Next:u}",
                batch.BatchId, newRetryCount, newRetryTime);
        }

        return count;
    }
}
