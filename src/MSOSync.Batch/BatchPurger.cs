using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class BatchPurger(AppDbContext db, IClock clock, ILogger<BatchPurger> logger)
{
    private const int DefaultRetentionDays = 30;
    private const string RetentionParam    = "batch.retention.days";

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var param = await db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == RetentionParam, ct);
        var days   = int.TryParse(param?.ParameterValue, out var d) ? d : DefaultRetentionDays;
        var cutoff = clock.UtcNow.AddDays(-days);

        var deleted = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Ok && b.CreateTime < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("BatchPurger deleted {Count} Ok batches older than {Cutoff:u}", deleted, cutoff);
        return deleted;
    }
}
