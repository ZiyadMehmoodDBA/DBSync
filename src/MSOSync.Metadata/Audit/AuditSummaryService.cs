using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Audit;

public sealed class AuditSummaryService(AppDbContext db) : IAuditSummaryService
{
    public async Task<AuditSummaryDto> GetSummaryAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var baseQ = db.Audits.AsNoTracking()
            .Where(a => a.CreateTime != null
                     && a.CreateTime >= from
                     && a.CreateTime <= to);

        var totalActions = await baseQ.CountAsync(ct);

        var failedOperations = await baseQ
            .CountAsync(a => a.ActionName != null && (
                a.ActionName.Contains("FAILURE") ||
                a.ActionName.Contains("FAILED")  ||
                a.ActionName.Contains("ERROR")   ||
                a.ActionName.Contains("LOCKED")  ||
                a.ActionName.Contains("REUSE")), ct);

        var permissionChanges = await baseQ
            .CountAsync(a => a.ActionName != null && (
                a.ActionName.Contains("PERMISSION") ||
                a.ActionName.Contains("ROLE")       ||
                a.ActionName.Contains("GRANT")      ||
                a.ActionName.Contains("REVOKE")), ct);

        // ByDay — group then zero-fill missing days
        var byDayRaw = await baseQ
            .GroupBy(a => a.CreateTime!.Value.Date)
            .Select(g => new
            {
                Date   = g.Key,
                Total  = g.Count(),
                Failed = g.Count(a => a.ActionName != null && (
                    a.ActionName.Contains("FAILURE") ||
                    a.ActionName.Contains("FAILED")  ||
                    a.ActionName.Contains("ERROR")   ||
                    a.ActionName.Contains("LOCKED")  ||
                    a.ActionName.Contains("REUSE")))
            })
            .ToListAsync(ct);

        var byDayDict = byDayRaw.ToDictionary(x => x.Date, x => (x.Total, x.Failed));
        var totalDays = (int)(to.Date - from.Date).TotalDays + 1;
        var byDay = Enumerable.Range(0, totalDays)
            .Select(i => from.Date.AddDays(i))
            .Select(d => byDayDict.TryGetValue(d, out var b)
                ? new DayBucket(DateOnly.FromDateTime(d), b.Total, b.Failed)
                : new DayBucket(DateOnly.FromDateTime(d), 0, 0))
            .ToList();

        // ByUser — top 10 descending (materialize then sort to avoid SQLite translation issues)
        var byUserRaw = await baseQ
            .Where(a => a.Username != null)
            .GroupBy(a => a.Username!)
            .Select(g => new { Username = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byUser = byUserRaw
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new UserBucket(x.Username, x.Count))
            .ToList();

        // ByEntityType — group by ObjectName, descending
        var byEntityTypeRaw = await baseQ
            .Where(a => a.ObjectName != null)
            .GroupBy(a => a.ObjectName!)
            .Select(g => new { EntityType = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byEntityType = byEntityTypeRaw
            .OrderByDescending(x => x.Count)
            .Select(x => new EntityTypeBucket(x.EntityType, x.Count))
            .ToList();

        // TopParameters — PARAMETER_* actions, top 10 by object name
        var topParametersRaw = await baseQ
            .Where(a => a.ActionName != null && a.ActionName.Contains("PARAMETER")
                     && a.ObjectName != null)
            .GroupBy(a => a.ObjectName!)
            .Select(g => new { ParameterName = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var topParameters = topParametersRaw
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new ParameterBucket(x.ParameterName, x.Count))
            .ToList();

        return new AuditSummaryDto(
            totalActions,
            failedOperations,
            permissionChanges,
            byDay.AsReadOnly(),
            byUser.AsReadOnly(),
            byEntityType.AsReadOnly(),
            topParameters.AsReadOnly());
    }
}
