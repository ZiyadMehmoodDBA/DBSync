namespace MSOSync.Persistence.Lock;

public interface IDatabaseLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct = default);
}
