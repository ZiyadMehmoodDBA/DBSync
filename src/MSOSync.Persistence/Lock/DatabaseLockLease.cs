using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Lock;

public sealed class DatabaseLockLease : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly string _lockName;
    private readonly string _owner;
    private bool _disposed;

    internal DatabaseLockLease(AppDbContext db, string lockName, string owner)
    {
        _db = db;
        _lockName = lockName;
        _owner = owner;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _db.Database.ExecuteSqlRawAsync(
            $"UPDATE msosync.sync_lock SET lock_owner = NULL, lock_time = NULL " +
            $"WHERE lock_name = '{_lockName}' AND lock_owner = '{_owner}'");
    }
}
