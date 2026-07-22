using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Timeline.Dtos;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations.Timeline;

public sealed class OperationTimelineService(AppDbContext db) : IOperationTimelineService
{
    public async Task<OperationTimelineDto> GetTimelineAsync(
        DateTime  from,
        DateTime  to,
        string[]? types,
        int       limit,
        CancellationToken ct = default)
    {
        var q = db.Operations
            .AsNoTracking()
            .Where(o => o.StartedAt >= from && o.StartedAt <= to);

        if (types is { Length: > 0 })
            q = q.Where(o => types.Contains(o.OperationType));

        // Fetch limit+1 to detect HasMore
        var fetchLimit = Math.Min(limit, 500) + 1;
        var rows = await q
            .OrderBy(o => o.StartedAt)
            .ThenBy(o => o.OperationId)
            .Take(fetchLimit)
            .Select(o => new OperationTimelineItemDto(
                o.OperationId,
                o.OperationType,
                o.Status,
                o.ProgressMessage ?? o.Summary ?? o.OperationType,
                o.StartedAt,
                o.CompletedAt,
                o.ProgressPercent))
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        if (hasMore) rows = rows.Take(limit).ToList();

        return new OperationTimelineDto(
            Items:         rows.AsReadOnly(),
            From:          from,
            To:            to,
            HasMore:       hasMore,
            ReturnedCount: rows.Count);
    }
}
