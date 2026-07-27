namespace MSOSync.Scheduler;

public enum SchedulerJobMode { Idle, Running, Standby }

/// <summary>
/// Snapshot of one job's scheduler lock state on this instance.
/// </summary>
public sealed record SchedulerJobStatus(
    string           JobName,
    SchedulerJobMode Mode,
    string?          LockOwner,
    DateTimeOffset?  LockedSince,
    DateTimeOffset   LastUpdated);
