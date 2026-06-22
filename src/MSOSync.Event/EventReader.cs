using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Event;

public sealed class EventReader(AppDbContext db) : IEventReader
{
    public async Task<IReadOnlyList<SyncDataEvent>> ReadAsync(int batchSize, CancellationToken ct = default)
    {
        return await db.DataEvents
            .AsNoTracking()
            .Where(e => !e.IsProcessed)
            .OrderBy(e => e.EventId)
            .Take(batchSize)
            .ToListAsync(ct);
    }
}
