using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

public sealed class SqlDistributedLockService(AppDbContext db) : IDistributedLockService
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var expiryMs = (long)expiry.TotalMilliseconds;

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE(), " +
            "    lock_expiry = DATEADD(ms, {1}, GETUTCDATE()) " +
            "WHERE lock_name = {2} " +
            "  AND (lock_owner IS NULL " +
            "    OR (lock_expiry IS NULL AND lock_time < DATEADD(MINUTE, -10, GETUTCDATE())) " +
            "    OR (lock_expiry IS NOT NULL AND lock_expiry < GETUTCDATE()))",
            new object[] { owner, expiryMs, resource }, ct);

        if (rows != 1) return null;

        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        return new SqlDistributedLock(this, resource, owner, expiresAt);
    }

    public async Task<bool> RenewAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var expiryMs = (long)expiry.TotalMilliseconds;

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_expiry = DATEADD(ms, {0}, GETUTCDATE()) " +
            "WHERE lock_name = {1} AND lock_owner = {2}",
            new object[] { expiryMs, resource, owner }, ct);

        return rows == 1;
    }

    public async Task ReleaseAsync(
        string resource, string owner, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL, lock_expiry = NULL " +
            "WHERE lock_name = {0} AND lock_owner = {1}",
            new object[] { resource, owner }, ct);
    }

    public async Task<bool> IsHeldAsync(
        string resource, CancellationToken ct = default)
    {
        var count = await db.Locks
            .AsNoTracking()
            .Where(l => l.LockName == resource
                     && l.LockOwner != null
                     && (l.LockExpiry == null || l.LockExpiry > DateTime.UtcNow))
            .CountAsync(ct);
        return count > 0;
    }
}
