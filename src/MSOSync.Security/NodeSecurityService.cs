using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Security;

public sealed class NodeSecurityService(AppDbContext db, BCryptPasswordHasher hasher)
{
    public async Task<bool> ValidateTokenAsync(
        string nodeId, string token, CancellationToken ct = default)
    {
        var sec = await db.NodeSecurities
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct);

        if (sec == null) return false;

        if (hasher.Verify(token, sec.CurrentTokenHash)) return true;
        if (sec.NextTokenHash != null && hasher.Verify(token, sec.NextTokenHash)) return true;

        return false;
    }
}
