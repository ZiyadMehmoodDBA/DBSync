using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.Audit;

public sealed class AuditService(AppDbContext db, IAuditChainService chainService) : IAuditService
{
    public async Task WriteAsync(
        string action, string detail, string actorUsername, CancellationToken ct = default)
    {
        var entry = new SyncAudit
        {
            ActionName = action,
            ObjectName = detail,
            Username   = actorUsername,
            CreateTime = DateTime.UtcNow,
        };

        // Compute hash chain BEFORE SaveChangesAsync so hashes are persisted with the row.
        await chainService.SetHashesAsync(entry, ct);

        db.Audits.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
