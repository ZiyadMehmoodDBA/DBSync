using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security;

namespace MSOSync.Metadata.Lifecycle;

public sealed class BootstrapTokenService(
    AppDbContext db,
    BCryptPasswordHasher hasher,
    IOptions<LifecycleOptions> options) : IBootstrapTokenService
{
    public async Task<string> IssueAsync(string nodeId, string issuedBy, CancellationToken ct = default)
    {
        await RevokeAllAsync(nodeId, ct);

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        db.NodeBootstrapTokens.Add(new SyncNodeBootstrapToken
        {
            NodeId = nodeId,
            TokenHash = hasher.Hash(raw),
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(options.Value.BootstrapTokenTtlHours),
            IssuedBy = issuedBy,
        });
        return raw;
    }

    public async Task<bool> ValidateAndConsumeAsync(string nodeId, string rawToken, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.NodeBootstrapTokens
            .Where(t => t.NodeId == nodeId && t.ConsumedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        var match = candidates.FirstOrDefault(t => hasher.Verify(rawToken, t.TokenHash));
        if (match is null) return false;

        match.ConsumedAt = now;
        return true;
    }

    public async Task RevokeAllAsync(string nodeId, CancellationToken ct = default)
    {
        var live = await db.NodeBootstrapTokens
            .Where(t => t.NodeId == nodeId && t.ConsumedAt == null && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in live) t.RevokedAt = DateTimeOffset.UtcNow;
    }
}
