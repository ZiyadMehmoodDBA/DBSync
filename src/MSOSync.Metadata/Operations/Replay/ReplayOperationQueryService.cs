using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations.Replay;

public sealed class ReplayOperationQueryService(AppDbContext db) : IReplayOperationQueryService
{
    public async Task<ReplayOperationDetailDto?> GetDetailAsync(
        Guid operationId, CancellationToken ct = default)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct);
        if (op is null) return null;

        var req = await db.ReplayRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OperationId == operationId, ct);

        var counts = await db.ReplayItems
            .Where(i => i.OperationId == operationId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total     = g.Count(),
                Completed = g.Count(i => i.Status == "Completed"),
                Failed    = g.Count(i => i.Status == "Failed"),
                Skipped   = g.Count(i => i.Status == "Skipped"),
            })
            .FirstOrDefaultAsync(ct);

        return new ReplayOperationDetailDto(
            OperationId:    op.OperationId,
            Status:         op.Status,
            Result:         op.Result,
            NodeId:         req?.NodeId ?? string.Empty,
            ReplayMode:     req?.ReplayMode ?? string.Empty,
            FromTime:       req?.FromTime ?? default,
            ToTime:         req?.ToTime ?? default,
            ChannelIds:     req?.ChannelIdsJson is null ? null
                            : JsonSerializer.Deserialize<string[]>(req.ChannelIdsJson),
            BatchIds:       req?.BatchIdsJson is null ? null
                            : JsonSerializer.Deserialize<long[]>(req.BatchIdsJson),
            TotalItems:     counts?.Total ?? 0,
            CompletedItems: counts?.Completed ?? 0,
            FailedItems:    counts?.Failed ?? 0,
            SkippedItems:   counts?.Skipped ?? 0,
            StartedAt:      op.StartedAt,
            CompletedAt:    op.CompletedAt);
    }

    public async Task<CursorPageResult<ReplayItemDto>> GetItemsAsync(
        Guid operationId, ReplayItemFilter filter, CancellationToken ct = default)
    {
        var query = db.ReplayItems.AsNoTracking()
            .Where(i => i.OperationId == operationId);

        if (filter.Status is not null)
            query = query.Where(i => i.Status == filter.Status);

        if (filter.Cursor is not null
            && Guid.TryParse(filter.Cursor, out var cursorId))
            query = query.Where(i => i.ItemId.CompareTo(cursorId) > 0);

        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var items    = await query.OrderBy(i => i.ItemId)
            .Take(pageSize + 1)
            .Select(i => new ReplayItemDto(
                i.ItemId, i.NodeId, i.ChannelId, i.EventCount,
                i.Status, i.ErrorMessage, i.SourceBatchId, i.ReplayBatchId))
            .ToListAsync(ct);

        var hasMore    = items.Count > pageSize;
        var page       = hasMore ? items.Take(pageSize).ToList() : items;
        var nextCursor = hasMore ? page[^1].ItemId.ToString() : null;

        return new CursorPageResult<ReplayItemDto>(page, nextCursor, hasMore, null);
    }
}
