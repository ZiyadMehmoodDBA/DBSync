using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Dashboard;

public sealed class DashboardQueryService(AppDbContext db) : IDashboardQueryService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var cutoff24h     = DateTime.UtcNow.AddHours(-24);
        var todayMidnight = DateTime.UtcNow.Date;

        var totalNodes         = await db.Nodes.AsNoTracking().CountAsync(ct);
        var reachableNodes     = await db.Nodes.AsNoTracking().CountAsync(n => n.ConnectivityStatus == ConnectivityStatus.Reachable,   ct);
        var degradedNodes      = await db.Nodes.AsNoTracking().CountAsync(n => n.ConnectivityStatus == ConnectivityStatus.Degraded,    ct);
        var unreachableNodes   = await db.Nodes.AsNoTracking().CountAsync(n => n.ConnectivityStatus == ConnectivityStatus.Unreachable, ct);
        var unknownNodes       = await db.Nodes.AsNoTracking().CountAsync(n => n.ConnectivityStatus == ConnectivityStatus.Unknown,     ct);
        var pendingEvents      = await db.DataEvents.AsNoTracking().LongCountAsync(e => !e.IsProcessed, ct);
        var queueDepth         = await db.OutgoingBatches.AsNoTracking().LongCountAsync(b => b.Status != 2, ct);
        var eventsToday        = await db.DataEvents.AsNoTracking().LongCountAsync(e => e.CreateTime >= todayMidnight, ct);
        var transportErrors24h = await db.BatchErrors.AsNoTracking().LongCountAsync(e => e.CreateTime >= cutoff24h, ct);

        return new DashboardSummaryDto(
            totalNodes,
            reachableNodes,
            degradedNodes,
            unreachableNodes,
            unknownNodes,
            pendingEvents,
            queueDepth,
            eventsToday,
            transportErrors24h,
            DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<ActivityItemDto>> GetActivityAsync(
        ActivityFilter filter, CancellationToken ct = default)
    {
        var auditItems = new List<ActivityItemDto>();
        var errorItems = new List<ActivityItemDto>();

        if (filter.Type is null or "audit")
        {
            var auditQ = db.Audits.AsNoTracking()
                .Where(a => a.CreateTime != null);
            if (filter.From is not null) auditQ = auditQ.Where(a => a.CreateTime >= filter.From);
            if (filter.To   is not null) auditQ = auditQ.Where(a => a.CreateTime <= filter.To);

            // Materialize first — EF Core 9 may not translate string interpolation in Select
            var auditRaw = await auditQ
                .OrderByDescending(a => a.CreateTime)
                .Take(filter.Limit)
                .Select(a => new { a.CreateTime, a.ActionName, a.ObjectName, a.Username })
                .ToListAsync(ct);

            auditItems = auditRaw.Select(a => new ActivityItemDto(
                "audit",
                a.CreateTime!.Value,
                null,
                $"{(a.ActionName ?? "action")} on {(a.ObjectName ?? "object")}",
                a.Username != null ? $"by {a.Username}" : null))
                .ToList();
        }

        if (filter.Type is null or "batch_error")
        {
            var errorQ = db.BatchErrors.AsNoTracking()
                .Join(db.OutgoingBatches.AsNoTracking(),
                      e => e.BatchId, b => b.BatchId,
                      (e, b) => new { e, b });
            if (filter.From is not null) errorQ = errorQ.Where(x => x.e.CreateTime >= filter.From);
            if (filter.To   is not null) errorQ = errorQ.Where(x => x.e.CreateTime <= filter.To);

            // Materialize first — string interpolation and conditional truncation may not translate
            var errorRaw = await errorQ
                .OrderByDescending(x => x.e.CreateTime)
                .Take(filter.Limit)
                .Select(x => new { x.e.CreateTime, x.b.NodeId, x.e.ErrorMessage })
                .ToListAsync(ct);

            errorItems = errorRaw.Select(x => new ActivityItemDto(
                "batch_error",
                x.CreateTime,
                x.NodeId,
                $"Batch error on {x.NodeId}",
                x.ErrorMessage != null
                    ? (x.ErrorMessage.Length > 200 ? x.ErrorMessage.Substring(0, 200) : x.ErrorMessage)
                    : null))
                .ToList();
        }

        return auditItems
            .Concat(errorItems)
            .OrderByDescending(i => i.Timestamp)
            .Take(filter.Limit)
            .ToList()
            .AsReadOnly();
    }
}
