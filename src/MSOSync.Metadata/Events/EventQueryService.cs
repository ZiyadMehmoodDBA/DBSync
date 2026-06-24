using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Events;

public sealed class EventQueryService(AppDbContext db) : IEventQueryService
{
    public async Task<PagedResult<EventSummaryDto>> GetEventsAsync(
        EventFilter filter, CancellationToken ct = default)
    {
        var q = db.DataEvents.AsNoTracking();

        if (filter.SourceNodeId is not null) q = q.Where(e => e.SourceNodeId == filter.SourceNodeId);
        if (filter.TriggerId    is not null) q = q.Where(e => e.TriggerId    == filter.TriggerId);
        if (filter.ChannelId    is not null) q = q.Where(e => e.ChannelId    == filter.ChannelId);
        if (filter.EventType    is not null) q = q.Where(e => e.EventType    == filter.EventType);
        if (filter.IsProcessed  is not null) q = q.Where(e => e.IsProcessed  == filter.IsProcessed);
        if (filter.From         is not null) q = q.Where(e => e.CreateTime   >= filter.From);
        if (filter.To           is not null) q = q.Where(e => e.CreateTime   <= filter.To);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(e => e.CreateTime)
            .Select(e => new EventSummaryDto(
                e.EventId,
                e.TriggerId,
                e.SourceNodeId,
                e.ChannelId,
                e.EventType,
                e.TableName,
                db.DataEventBatches
                    .Where(deb => deb.EventId == e.EventId)
                    .Max(deb => (long?)deb.BatchId),
                e.CreateTime,
                e.IsProcessed))
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedResult<EventSummaryDto>(items.AsReadOnly(), filter.Page, filter.PageSize, total);
    }

    public async Task<EventDetailDto?> GetEventByIdAsync(
        long eventId, CancellationToken ct = default)
    {
        var e = await db.DataEvents.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .FirstOrDefaultAsync(ct);

        if (e is null) return null;

        var batchId = await db.DataEventBatches
            .AsNoTracking()
            .Where(deb => deb.EventId == eventId)
            .MaxAsync(deb => (long?)deb.BatchId, ct);

        return new EventDetailDto(
            e.EventId, e.TriggerId, e.SourceNodeId, e.ChannelId,
            e.EventType, e.TableName, e.PkData, e.RowData, e.TransactionId,
            batchId, e.CreateTime, e.IsProcessed);
    }
}
