using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Pagination;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Metadata.Audit;

public sealed class AuditQueryService(
    IPlatformRepository<SyncAudit> auditRepo,
    CursorSigner                   cursorSigner) : IAuditQueryService
{
    public async Task<CursorPageResult<AuditDto>> GetAuditsAsync(
        AuditFilter filter, CancellationToken ct = default)
    {
        var baseQ = auditRepo.QueryAll()
            .Where(a => a.CreateTime != null);

        if (filter.Username   is not null) baseQ = baseQ.Where(a => a.Username   == filter.Username);
        if (filter.ActionName is not null) baseQ = baseQ.Where(a => a.ActionName == filter.ActionName);
        if (filter.From       is not null) baseQ = baseQ.Where(a => a.CreateTime >= filter.From);
        if (filter.To         is not null) baseQ = baseQ.Where(a => a.CreateTime <= filter.To);

        var q = baseQ;
        if (filter.Cursor is not null)
        {
            var (cursorId, _) = cursorSigner.Decode(filter.Cursor);
            q = q.Where(a => a.AuditId < cursorId);
        }

        var pageSize = filter.PageSize;
        var rows = await q
            .OrderByDescending(a => a.AuditId)
            .Take(pageSize + 1)
            .Select(a => new AuditDto(
                a.AuditId,
                a.Username,
                a.ActionName,
                a.ObjectName,
                a.CorrelationId,
                a.CreateTime!.Value))
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows = rows.Take(pageSize).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[^1];
            nextCursor = cursorSigner.Encode(last.AuditId, last.CreateTime.Ticks);
        }

        int? totalCount = null;
        if (filter.IncludeTotalCount)
            totalCount = await baseQ.CountAsync(ct);

        return new CursorPageResult<AuditDto>(rows.AsReadOnly(), nextCursor, hasMore, totalCount);
    }

    public async Task<AuditDto?> GetAuditByIdAsync(long auditId, CancellationToken ct = default)
    {
        var a = await auditRepo.QueryAll()
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
