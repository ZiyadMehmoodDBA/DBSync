namespace MSOSync.Scheduler;

/// <summary>
/// Represents an acquired distributed scheduler lock for one job tick.
/// Disposal releases the lock immediately and cancels the renewal loop.
/// </summary>
public interface ISchedulerLock : IAsyncDisposable
{
    /// <summary>Name of the job this lock was acquired for.</summary>
    string JobName { get; }

    /// <summary>UTC timestamp when the lock was acquired.</summary>
    DateTimeOffset AcquiredAt { get; }

    /// <summary>Identity of the instance holding the lock ("MachineName:PID").</summary>
    string Owner { get; }
}
