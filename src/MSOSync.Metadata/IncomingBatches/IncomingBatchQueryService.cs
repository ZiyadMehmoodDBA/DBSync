using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed class IncomingBatchQueryService(AppDbContext db, CursorSigner cursorSigner) : IIncomingBatchQueryService
{
    public async Task<CursorPageResult<IncomingBatchSummaryDto>> GetIncomingBatchesAsync(
        IncomingBatchFilter filter, CancellationToken ct = default)
    {
        var baseQ = db.IncomingBatches.AsNoTracking();

        if (filter.SourceNodeId is not null) baseQ = baseQ.Where(b => b.SourceNodeId == filter.SourceNodeId);
        if (filter.ChannelId    is not null) baseQ = baseQ.Where(b => b.ChannelId    == filter.ChannelId);
        if (filter.Status       is not null) baseQ = baseQ.Where(b => b.Status       == filter.Status);
        if (filter.From         is not null) baseQ = baseQ.Where(b => b.ReceivedTime >= filter.From);
        if (filter.To           is not null) baseQ = baseQ.Where(b => b.ReceivedTime <= filter.To);

        var q = baseQ;
        if (filter.Cursor is not null)
        {
            var (cursorId, _) = cursorSigner.Decode(filter.Cursor);
            q = q.Where(b => b.BatchId < cursorId);
        }

        var pageSize = filter.PageSize;
        var rows = await q
            .OrderByDescending(b => b.BatchId)
            .Take(pageSize + 1)
            .Select(b => new IncomingBatchSummaryDto(
                b.BatchId,
                b.SourceNodeId,
                b.ChannelId,
                b.Status,
                b.RowCount,
                b.BatchSequence,
                b.ReceivedTime,
                b.ApplyTimeMs))
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows = rows.Take(pageSize).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[^1];
            nextCursor = cursorSigner.Encode(last.BatchId, last.ReceivedTime.Ticks);
        }

        int? totalCount = null;
        if (filter.IncludeTotalCount)
            totalCount = await baseQ.CountAsync(ct);

        return new CursorPageResult<IncomingBatchSummaryDto>(rows.AsReadOnly(), nextCursor, hasMore, totalCount);
    }

    public async Task<IncomingBatchDetailDto?> GetIncomingBatchByIdAsync(
        long batchId, CancellationToken ct = default)
    {
        var b = await db.IncomingBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BatchId == batchId, ct);

        if (b is null) return null;

        var applyTimeMs = b.ApplyTimeMs
            ?? (b.AppliedTime.HasValue
                ? (long)(b.AppliedTime.Value - b.ReceivedTime).TotalMilliseconds
                : (long?)null);

        return new IncomingBatchDetailDto(
            b.BatchId, b.SourceNodeId, b.ChannelId, b.Status,
            b.RowCount, b.BatchSequence, b.ReceivedTime,
            b.LoadTime, b.ExtractTime, b.AppliedTime, applyTimeMs);
    }
}
