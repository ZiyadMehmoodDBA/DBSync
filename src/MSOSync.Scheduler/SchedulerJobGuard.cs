using Microsoft.Extensions.Logging;

namespace MSOSync.Scheduler;

/// <summary>
/// Runs <paramref name="work"/> under a distributed scheduler lock for <paramref name="jobName"/>.
/// If the lock cannot be acquired (another instance holds it), logs at Debug and returns immediately.
/// Health state transitions: Running → (work executes) → Idle on the active instance;
/// Standby on instances that lose the lock acquisition race.
/// </summary>
public static class SchedulerJobGuard
{
    /// <summary>
    /// Tries to acquire the distributed scheduler lock for <paramref name="jobName"/>,
    /// runs <paramref name="work"/> if successful, then releases the lock.
    /// Standby instances skip <paramref name="work"/> entirely with no side effects.
    /// </summary>
    /// <param name="jobName">Job name — used as the lock key suffix (e.g., "SyncJob").</param>
    /// <param name="lockFactory">Factory that acquires the distributed lock.</param>
    /// <param name="health">Health reporter — records Running, Standby, and Idle transitions.</param>
    /// <param name="logger">Logger for debug/warning messages.</param>
    /// <param name="work">The job body. Receives the outer cancellation token.</param>
    /// <param name="ct">Cancellation token from the outer BackgroundService loop.</param>
    public static async Task RunAsync(
        string                        jobName,
        ISchedulerLockFactory         lockFactory,
        ISchedulerHealthReporter      health,
        ILogger                       logger,
        Func<CancellationToken, Task> work,
        CancellationToken             ct)
    {
        await using var schedulerLock = await lockFactory.TryAcquireAsync(jobName, ct);

        if (schedulerLock is null)
        {
            logger.LogDebug("{Job}: lock held by another instance — skipping tick", jobName);
            health.RecordStandby(jobName);
            return;
        }

        health.RecordRunning(jobName, schedulerLock.Owner, schedulerLock.AcquiredAt);
        logger.LogDebug(
            "{Job}: acquired scheduler lock (owner={Owner})", jobName, schedulerLock.Owner);

        try
        {
            await work(ct);
        }
        finally
        {
            health.RecordIdle(jobName);
            // IAsyncDisposable on schedulerLock cancels renewal and releases the DB row here.
        }
    }
}
