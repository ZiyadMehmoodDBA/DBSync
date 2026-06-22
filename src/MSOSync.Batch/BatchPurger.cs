using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class BatchPurger(AppDbContext db, IClock clock, ILogger<BatchPurger> logger)
{
    private const int DefaultRetentionDays = 30;
    private const string RetentionParam    = "batch.retention.days";

    private const int MaxRetries = 5; // matches RetryProcessor.MaxRetries

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var param = await db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == RetentionParam, ct);
        var days   = int.TryParse(param?.ParameterValue, out var d) ? d : DefaultRetentionDays;
        var cutoff = clock.UtcNow.AddDays(-days);

        // Delete junction rows for batches that will be purged (Ok OR exhausted Error)
        var batchIdsToPurge = await db.OutgoingBatches
            .Where(b => b.CreateTime < cutoff &&
                       (b.Status == (byte)BatchStatus.Ok ||
                       (b.Status == (byte)BatchStatus.Error && b.RetryCount >= MaxRetries)))
            .Select(b => b.BatchId)
            .ToListAsync(ct);

        if (batchIdsToPurge.Count > 0)
        {
            await db.DataEventBatches
                .Where(l => batchIdsToPurge.Contains(l.BatchId))
                .ExecuteDeleteAsync(ct);
        }

        var deleted = await db.OutgoingBatches
            .Where(b => b.CreateTime < cutoff &&
                       (b.Status == (byte)BatchStatus.Ok ||
                       (b.Status == (byte)BatchStatus.Error && b.RetryCount >= MaxRetries)))
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("BatchPurger deleted {Count} batches (Ok or exhausted Error) older than {Cutoff:u}", deleted, cutoff);
        return deleted;
    }
}
