using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.Audit;

public interface IAuditQueryService
{
    Task<PagedResult<AuditDto>> GetAuditsAsync(AuditFilter filter, CancellationToken ct);
    Task<AuditDto?>             GetAuditByIdAsync(long auditId, CancellationToken ct);
}
