using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Event;

public sealed class EventPurger(AppDbContext db, IClock clock, ILogger<EventPurger> logger) : IEventPurger
{
    private const int DefaultRetentionDays = 30;
    private const string RetentionParam = "event.retention.days";

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var param = await db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == RetentionParam, ct);

        var retentionDays = int.TryParse(param?.ParameterValue, out var d) ? d : DefaultRetentionDays;
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        // Delete junction rows for events that will be purged
        var eventIdsToPurge = await db.DataEvents
            .Where(e => e.IsProcessed && e.CreateTime < cutoff)
            .Select(e => e.EventId)
            .ToListAsync(ct);

        if (eventIdsToPurge.Count > 0)
        {
            await db.DataEventBatches
                .Where(l => eventIdsToPurge.Contains(l.EventId))
                .ExecuteDeleteAsync(ct);
        }

        var deleted = await db.DataEvents
            .Where(e => e.IsProcessed && e.CreateTime < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("EventPurger deleted {Count} events older than {Cutoff:u}", deleted, cutoff);
        return deleted;
    }
}
