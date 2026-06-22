namespace MSOSync.Persistence.Lock;

public interface IDatabaseLockProvider
{
    Task<DatabaseLockLease?> TryAcquireAsync(string lockName, CancellationToken ct = default);
}
