using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Lock;

public sealed class DatabaseLockProvider(AppDbContext db) : IDatabaseLockProvider
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task<DatabaseLockLease?> TryAcquireAsync(string lockName, CancellationToken ct = default)
    {
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE() " +
            "WHERE lock_name = {1} " +
            "  AND (lock_owner IS NULL OR lock_time < DATEADD(MINUTE, -10, GETUTCDATE()))",
            new object[] { owner, lockName },
            ct);

        return rows == 1 ? new DatabaseLockLease(db, Schema, lockName, owner) : null;
    }
}
