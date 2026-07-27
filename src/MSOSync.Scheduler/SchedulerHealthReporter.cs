using System.Collections.Concurrent;

namespace MSOSync.Scheduler;

/// <summary>
/// Thread-safe singleton that tracks per-job scheduler lock state using
/// a <see cref="ConcurrentDictionary"/>. No external storage — state is
/// in-process only and resets on restart.
/// </summary>
public sealed class SchedulerHealthReporter : ISchedulerHealthReporter
{
    private readonly ConcurrentDictionary<string, SchedulerJobStatus> _statuses = new();

    public void RecordRunning(string jobName, string owner, DateTimeOffset acquiredAt)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Running, owner, acquiredAt, DateTimeOffset.UtcNow);

    public void RecordStandby(string jobName)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Standby, null, null, DateTimeOffset.UtcNow);

    public void RecordIdle(string jobName)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Idle, null, null, DateTimeOffset.UtcNow);

    public SchedulerJobStatus[] GetAll()
        => [.. _statuses.Values];

    public SchedulerJobStatus GetOne(string jobName)
        => _statuses.GetValueOrDefault(jobName)
           ?? new SchedulerJobStatus(jobName, SchedulerJobMode.Idle, null, null, DateTimeOffset.UtcNow);
}
