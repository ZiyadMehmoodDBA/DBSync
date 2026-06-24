using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed class IncomingBatchQueryService(AppDbContext db) : IIncomingBatchQueryService
{
    public async Task<PagedResult<IncomingBatchSummaryDto>> GetIncomingBatchesAsync(
        IncomingBatchFilter filter, CancellationToken ct = default)
    {
        var q = db.IncomingBatches.AsNoTracking();

        if (filter.SourceNodeId is not null) q = q.Where(b => b.SourceNodeId == filter.SourceNodeId);
        if (filter.ChannelId    is not null) q = q.Where(b => b.ChannelId    == filter.ChannelId);
        if (filter.Status       is not null) q = q.Where(b => b.Status       == filter.Status);
        if (filter.From         is not null) q = q.Where(b => b.ReceivedTime >= filter.From);
        if (filter.To           is not null) q = q.Where(b => b.ReceivedTime <= filter.To);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(b => b.ReceivedTime)
            .Select(b => new IncomingBatchSummaryDto(
                b.BatchId,
                b.SourceNodeId,
                b.ChannelId,
                b.Status,
                b.RowCount,
                b.BatchSequence,
                b.ReceivedTime,
                b.ApplyTimeMs))
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedResult<IncomingBatchSummaryDto>(
            items.AsReadOnly(), filter.Page, filter.PageSize, total);
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
