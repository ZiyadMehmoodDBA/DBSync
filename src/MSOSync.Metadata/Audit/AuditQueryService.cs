using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Audit;

public sealed class AuditQueryService(AppDbContext db) : IAuditQueryService
{
    public async Task<PagedResult<AuditDto>> GetAuditsAsync(
        AuditFilter filter, CancellationToken ct = default)
    {
        var q = db.Audits.AsNoTracking()
            .Where(a => a.CreateTime != null);

        if (filter.Username   is not null) q = q.Where(a => a.Username   == filter.Username);
        if (filter.ActionName is not null) q = q.Where(a => a.ActionName == filter.ActionName);
        if (filter.From       is not null) q = q.Where(a => a.CreateTime >= filter.From);
        if (filter.To         is not null) q = q.Where(a => a.CreateTime <= filter.To);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(a => a.CreateTime)
            .Select(a => new AuditDto(
                a.AuditId,
                a.Username,
                a.ActionName,
                a.ObjectName,
                a.CorrelationId,
                a.CreateTime!.Value))
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedResult<AuditDto>(items.AsReadOnly(), filter.Page, filter.PageSize, total);
    }

    public async Task<AuditDto?> GetAuditByIdAsync(long auditId, CancellationToken ct = default)
    {
        var a = await db.Audits.AsNoTracking()
            .Where(x => x.AuditId == auditId && x.CreateTime != null)
            .FirstOrDefaultAsync(ct);

        if (a is null) return null;

        return new AuditDto(
            a.AuditId,
            a.Username,
            a.ActionName,
            a.ObjectName,
            a.CorrelationId,
            a.CreateTime!.Value);
    }
}
