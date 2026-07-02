namespace MSOSync.Metadata.Audit;

public interface IAuditSummaryService
{
    Task<AuditSummaryDto> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
