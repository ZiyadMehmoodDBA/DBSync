using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Lock;

public sealed class DatabaseLockLease : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly string _schema;
    private readonly string _lockName;
    private readonly string _owner;
    private bool _disposed;

    internal DatabaseLockLease(AppDbContext db, string schema, string lockName, string owner)
    {
        _db = db;
        _schema = schema;
        _lockName = lockName;
        _owner = owner;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{_schema}].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL " +
            "WHERE lock_name = {0} AND lock_owner = {1}",
            new object[] { _lockName, _owner },
            CancellationToken.None);
    }
}
