using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.Audit;

public interface IAuditQueryService
{
    Task<CursorPageResult<AuditDto>> GetAuditsAsync(AuditFilter filter, CancellationToken ct);
    Task<AuditDto?>             GetAuditByIdAsync(long auditId, CancellationToken ct);
    Task<CursorPageResult<AuditDto>> GetEntityHistoryAsync(
        string  objectName,
        string? cursor,
        int     pageSize,
        CancellationToken ct = default);
}
