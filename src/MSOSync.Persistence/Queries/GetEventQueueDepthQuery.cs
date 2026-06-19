using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Queries;

public sealed class GetEventQueueDepthQuery(AppDbContext db)
{
    public Task<Dictionary<string, int>> ExecuteAsync(CancellationToken ct = default)
        => db.DataEvents
            .AsNoTracking()
            .Where(e => !e.IsProcessed)
            .GroupBy(e => e.ChannelId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
}
