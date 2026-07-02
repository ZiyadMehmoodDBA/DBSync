using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Export;

public sealed class ExportAuditService(AppDbContext db, ICurrentUserService currentUser) : IExportAuditService
{
    public async Task WriteAsync(string resource, string format, int rowCount, long durationMs, CancellationToken ct = default)
    {
        db.Audits.Add(new SyncAudit
        {
            ActionName = $"EXPORT_{resource.ToUpperInvariant().Replace('-', '_')}",
            ObjectName = $"{resource}|{format}|{rowCount}|{durationMs}",
            Username   = currentUser.GetCurrentUsername(),
            CreateTime = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
