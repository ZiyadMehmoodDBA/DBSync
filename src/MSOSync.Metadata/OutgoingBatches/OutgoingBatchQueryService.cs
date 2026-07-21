using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.OutgoingBatches;

public sealed class OutgoingBatchQueryService(AppDbContext db) : IOutgoingBatchQueryService
{
    public async Task<OutgoingBatchPage> GetBatchesAsync(
        OutgoingBatchQueryFilter filter, CancellationToken ct = default)
    {
        var query = db.OutgoingBatches.AsNoTracking();

        if (!string.IsNullOrEmpty(filter.NodeId))    query = query.Where(b => b.NodeId == filter.NodeId);
        if (!string.IsNullOrEmpty(filter.ChannelId)) query = query.Where(b => b.ChannelId == filter.ChannelId);
        if (filter.Status is not null)               query = query.Where(b => b.Status == filter.Status);

        query = (filter.SortBy, filter.SortDirection.ToLowerInvariant()) switch
        {
            ("batchId", "asc")  => query.OrderBy(b => b.BatchId),
            ("batchId", _)      => query.OrderByDescending(b => b.BatchId),
            ("status",  "asc")  => query.OrderBy(b => b.Status),
            ("status",  _)      => query.OrderByDescending(b => b.Status),
            (_,         "asc")  => query.OrderBy(b => b.CreateTime),
            _                   => query.OrderByDescending(b => b.CreateTime),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(b => new OutgoingBatchRow(
                b.BatchId, b.Status, b.NodeId, b.ChannelId,
                b.CreateTime, b.SentTime, b.AckTime, b.RetryCount, b.RowCount, null))
            .ToListAsync(ct);

        return new OutgoingBatchPage(items, total);
    }

    public async Task<OutgoingBatchRow?> GetBatchByIdAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await db.OutgoingBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BatchId == batchId, ct);
        if (batch is null) return null;

        var error = await db.BatchErrors.AsNoTracking()
            .Where(e => e.BatchId == batchId)
            .OrderByDescending(e => e.ErrorId)
            .Select(e => e.ErrorMessage)
            .FirstOrDefaultAsync(ct);

        return new OutgoingBatchRow(
            batch.BatchId, batch.Status, batch.NodeId, batch.ChannelId,
            batch.CreateTime, batch.SentTime, batch.AckTime, batch.RetryCount, batch.RowCount, error);
    }
}
