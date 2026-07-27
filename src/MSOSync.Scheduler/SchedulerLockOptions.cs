namespace MSOSync.Scheduler;

public sealed class SchedulerLockOptions
{
    public const string Section = "Scheduler:Lock";

    /// <summary>Lock TTL in seconds. Default 120. Must be >= 3x RenewalIntervalSeconds.</summary>
    public int TtlSeconds { get; init; } = 120;

    /// <summary>How often to renew the lock while a job is running (seconds). Default 10.</summary>
    public int RenewalIntervalSeconds { get; init; } = 10;

    /// <summary>Prefix prepended to every job lock name. Default "scheduler:".</summary>
    public string LockPrefix { get; init; } = "scheduler:";
}
