using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

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

    public NodeProvisionResult PrepareToken(string nodeId)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = hasher.Hash(raw);

        var existing = db.NodeSecurities.Local.FirstOrDefault(s => s.NodeId == nodeId)
            ?? db.NodeSecurities.Find(nodeId);

        if (existing != null)
        {
            existing.CurrentTokenHash = hash;
            existing.NextTokenHash = null;
            existing.RotationScheduled = null;
        }
        else
        {
            db.NodeSecurities.Add(new SyncNodeSecurity
            {
                NodeId = nodeId,
                CurrentTokenHash = hash,
                CreatedTime = DateTime.UtcNow
            });
        }

        return new NodeProvisionResult(nodeId, raw);
    }
}
