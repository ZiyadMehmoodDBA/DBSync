using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Audit;

public sealed class AuditService(AppDbContext db) : IAuditService
{
    public async Task WriteAsync(
        string action, string detail, string actorUsername, CancellationToken ct = default)
    {
        db.Audits.Add(new SyncAudit
        {
            ActionName = action,
            ObjectName = detail,
            Username   = actorUsername,
            CreateTime = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
