using MSOSync.Persistence.Entities;

namespace MSOSync.Security;

public interface IAuditChainService
{
    string ComputeHash(string? prevHash, SyncAudit entry);
    Task SetHashesAsync(SyncAudit entry, CancellationToken ct = default);
    Task<(bool IsValid, long? FirstBrokenId)> VerifyChainAsync(CancellationToken ct = default);
}
