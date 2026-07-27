namespace MSOSync.Scheduler;

/// <summary>
/// Tracks per-job scheduler lock state for this running instance.
///
/// <list type="bullet">
/// <item><description>Running — this instance holds the lock; job is executing.</description></item>
/// <item><description>Standby — another instance holds the lock; this instance skipped the tick.</description></item>
/// <item><description>Idle — lock not held by anyone (between ticks or before first tick).</description></item>
/// </list>
/// </summary>
public interface ISchedulerHealthReporter
{
    /// <summary>Records that this instance acquired the lock and is running the job.</summary>
    void RecordRunning(string jobName, string owner, DateTimeOffset acquiredAt);

    /// <summary>Records that another instance holds the lock; this instance skipped this tick.</summary>
    void RecordStandby(string jobName);

    /// <summary>Records that the job completed and the lock was released (between ticks).</summary>
    void RecordIdle(string jobName);

    /// <summary>Returns status snapshots for all jobs seen so far on this instance.</summary>
    SchedulerJobStatus[] GetAll();

    /// <summary>Returns the status snapshot for a specific job, defaulting to Idle if unseen.</summary>
    SchedulerJobStatus GetOne(string jobName);
}
