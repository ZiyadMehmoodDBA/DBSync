namespace MSOSync.Scheduler;

/// <summary>
/// Creates distributed scheduler lock instances.
/// Returns null if another instance already holds the lock.
/// </summary>
public interface ISchedulerLockFactory
{
    /// <summary>
    /// Attempts to acquire "scheduler:{jobName}" lock.
    /// Returns a live <see cref="ISchedulerLock"/> (with renewal loop started) on success,
    /// or null if the lock is held by another instance.
    /// </summary>
    Task<ISchedulerLock?> TryAcquireAsync(string jobName, CancellationToken ct);
}
