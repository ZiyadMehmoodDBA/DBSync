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

        var effectiveUsernames   = filter.Usernames?.Length  > 0 ? filter.Usernames  : (filter.Username   != null ? [filter.Username]   : null);
        var effectiveActions     = filter.ActionNames?.Length > 0 ? filter.ActionNames : (filter.ActionName != null ? [filter.ActionName] : null);
        var effectiveObjectNames = filter.ObjectNames?.Length > 0 ? filter.ObjectNames : null;

        if (effectiveUsernames   is not null) baseQ = baseQ.Where(a => effectiveUsernames.Contains(a.Username));
        if (effectiveActions     is not null) baseQ = baseQ.Where(a => effectiveActions.Contains(a.ActionName));
        if (effectiveObjectNames is not null) baseQ = baseQ.Where(a => effectiveObjectNames.Contains(a.ObjectName));
        if (filter.From          is not null) baseQ = baseQ.Where(a => a.CreateTime >= filter.From);
        if (filter.To            is not null) baseQ = baseQ.Where(a => a.CreateTime <= filter.To);

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

    public async Task<CursorPageResult<AuditDto>> GetEntityHistoryAsync(
        string  objectName,
        string? cursor,
        int     pageSize,
        CancellationToken ct = default)
    {
        var baseQ = auditRepo.QueryAll()
            .Where(a => a.CreateTime != null && a.ObjectName == objectName);

        var q = baseQ;
        if (cursor is not null)
        {
            var (cursorId, _) = cursorSigner.Decode(cursor);
            q = q.Where(a => a.AuditId < cursorId);
        }

        var size = Math.Clamp(pageSize, 1, 200);
        var rows = await q
            .OrderByDescending(a => a.AuditId)
            .Take(size + 1)
            .Select(a => new AuditDto(
                a.AuditId, a.Username, a.ActionName,
                a.ObjectName, a.CorrelationId, a.CreateTime!.Value))
            .ToListAsync(ct);

        var hasMore = rows.Count > size;
        if (hasMore) rows = rows.Take(size).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[^1];
            nextCursor = cursorSigner.Encode(last.AuditId, last.CreateTime.Ticks);
        }

        return new CursorPageResult<AuditDto>(rows.AsReadOnly(), nextCursor, hasMore, null);
    }
}
