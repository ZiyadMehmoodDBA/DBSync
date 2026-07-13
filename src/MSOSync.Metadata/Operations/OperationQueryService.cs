using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations;

public sealed class OperationQueryService(AppDbContext db, CursorSigner cursorSigner) : IOperationQueryService
{
    public async Task<OperationPageDto> GetPageAsync(OperationFilter filter, CancellationToken ct)
    {
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = db.Operations.AsNoTracking().AsQueryable();

        if (filter.Types is { Length: > 0 })
            query = query.Where(o => filter.Types.Contains(o.OperationType));

        if (filter.Statuses is { Length: > 0 })
            query = query.Where(o => filter.Statuses.Contains(o.Status));

        if (filter.Sources is { Length: > 0 })
            query = query.Where(o => filter.Sources.Contains(o.Source));

        if (filter.From.HasValue)
            query = query.Where(o => o.StartedAt >= filter.From.Value);

        if (filter.To.HasValue)
            query = query.Where(o => o.StartedAt <= filter.To.Value);

        if (!string.IsNullOrEmpty(filter.InitiatedBy)
            && Guid.TryParse(filter.InitiatedBy, out var initiatedByGuid))
            query = query.Where(o => o.InitiatedBy == initiatedByGuid);

        // Cursor is the StartedAt tick value of the last item, HMAC-signed
        if (!string.IsNullOrEmpty(filter.Cursor))
        {
            try
            {
                var (_, cursorTick) = cursorSigner.Decode(filter.Cursor);
                var cursorDate = new DateTime(cursorTick, DateTimeKind.Utc);
                query = query.Where(o => o.StartedAt < cursorDate);
            }
            catch (ArgumentException) { /* invalid cursor — ignore */ }
        }

        query = query.OrderByDescending(o => o.StartedAt);

        // Fetch one extra to detect next page
        var rows = await query.Take(pageSize + 1).ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > pageSize)
        {
            rows.RemoveAt(pageSize);
            nextCursor = cursorSigner.Encode(0L, rows[^1].StartedAt.Ticks);
        }

        // Compute queue position for Pending operations
        var pendingRank = 0;
        var items = rows.Select(o =>
        {
            int? queuePos = null;
            if (o.Status == "Pending") queuePos = ++pendingRank;
            return new OperationDto(
                o.OperationId, o.OperationType, o.ReferenceId,
                o.Status, o.Result, o.Source,
                o.ProgressPercent, o.ProgressMessage,
                o.CorrelationId, o.InitiatedBy,
                o.MetadataJson, o.Summary,
                o.CanCancel, o.CanRetry,
                o.StartedAt, o.CompletedAt,
                QueuePosition: queuePos);
        }).ToList();

        return new OperationPageDto(items, nextCursor, TotalCount: null);
    }

    public async Task<OperationDetailDto?> GetDetailAsync(Guid operationId, CancellationToken ct)
    {
        var o = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == operationId, ct);
        if (o is null) return null;

        return new OperationDetailDto(
            o.OperationId, o.OperationType, o.ReferenceId,
            o.Status, o.Result, o.Source,
            o.ProgressPercent, o.ProgressMessage,
            o.CorrelationId, o.InitiatedBy,
            o.MetadataJson, o.Summary,
            o.CanCancel, o.CanRetry,
            o.StartedAt, o.CompletedAt);
    }

}
