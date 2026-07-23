namespace MSOSync.Common.Locks;

/// <summary>
/// Provider-agnostic distributed lock service.
/// Lock acquisition is non-blocking: TryAcquireAsync returns null if the lock
/// cannot be taken immediately. Callers are responsible for retry if desired.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Attempt to acquire the named lock. Returns null if the lock is held by
    /// another owner. The returned handle must be disposed to release the lock.
    /// </summary>
    Task<IDistributedLock?> TryAcquireAsync(
        string            resource,
        string            owner,
        TimeSpan          expiry,
        CancellationToken ct = default);

    /// <summary>
    /// Extend the expiry of an existing lock held by <paramref name="owner"/>.
    /// Returns false if the lock is not currently held by that owner.
    /// </summary>
    Task<bool> RenewAsync(
        string            resource,
        string            owner,
        TimeSpan          expiry,
        CancellationToken ct = default);

    /// <summary>
    /// Release the lock. No-op if the lock is not held by owner.
    /// </summary>
    Task ReleaseAsync(
        string            resource,
        string            owner,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if the named lock is currently held by any owner and has
    /// not expired. Used by diagnostic/admin endpoints only.
    /// </summary>
    Task<bool> IsHeldAsync(
        string            resource,
        CancellationToken ct = default);
}
