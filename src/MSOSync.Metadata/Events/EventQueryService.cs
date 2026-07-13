using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Events;

public sealed class EventQueryService(AppDbContext db, CursorSigner cursorSigner) : IEventQueryService
{
    public async Task<CursorPageResult<EventSummaryDto>> GetEventsAsync(
        EventFilter filter, CancellationToken ct = default)
    {
        var baseQ = db.DataEvents.AsNoTracking();

        if (filter.SourceNodeId is not null) baseQ = baseQ.Where(e => e.SourceNodeId == filter.SourceNodeId);
        if (filter.TriggerId    is not null) baseQ = baseQ.Where(e => e.TriggerId    == filter.TriggerId);
        if (filter.ChannelId    is not null) baseQ = baseQ.Where(e => e.ChannelId    == filter.ChannelId);
        if (filter.EventType    is not null) baseQ = baseQ.Where(e => e.EventType    == filter.EventType);
        if (filter.IsProcessed  is not null) baseQ = baseQ.Where(e => e.IsProcessed  == filter.IsProcessed);
        if (filter.From         is not null) baseQ = baseQ.Where(e => e.CreateTime   >= filter.From);
        if (filter.To           is not null) baseQ = baseQ.Where(e => e.CreateTime   <= filter.To);

        var q = baseQ;
        if (filter.Cursor is not null)
        {
            var (cursorId, _) = cursorSigner.Decode(filter.Cursor);
            q = q.Where(e => e.EventId < cursorId);
        }

        var pageSize = filter.PageSize;
        var rows = await q
            .OrderByDescending(e => e.EventId)
            .Take(pageSize + 1)
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
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows = rows.Take(pageSize).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[^1];
            nextCursor = cursorSigner.Encode(last.EventId, last.CreateTime.Ticks);
        }

        int? totalCount = null;
        if (filter.IncludeTotalCount)
            totalCount = await baseQ.CountAsync(ct);

        return new CursorPageResult<EventSummaryDto>(rows.AsReadOnly(), nextCursor, hasMore, totalCount);
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
