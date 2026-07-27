namespace MSOSync.Scheduler.Options;

public sealed class AdaptivePollingOptions
{
    public const string Section = "AdaptivePolling";

    /// <summary>Floor: fastest poll rate, applied when node is continuously busy.</summary>
    public int MinIntervalSeconds { get; init; } = 5;

    /// <summary>Ceiling: slowest poll rate, applied when node has been idle long enough.</summary>
    public int MaxIntervalSeconds { get; init; } = 300;

    /// <summary>Starting interval for a node with no history.</summary>
    public int BaseIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Multiplier applied per consecutive idle cycle.
    /// Interval grows: base × BackoffMultiplier^idleCount, capped at MaxIntervalSeconds.
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Multiplier applied per consecutive error cycle (independent of idle backoff).
    /// Applied on top of base: base × ErrorBackoffMultiplier^errorCount.
    /// </summary>
    public double ErrorBackoffMultiplier { get; init; } = 2.0;

    /// <summary>Maximum consecutive error backoffs before interval is capped at MaxIntervalSeconds.</summary>
    public int MaxErrorBackoffCount { get; init; } = 5;

    /// <summary>
    /// Jitter range applied to error-backoff intervals as a fraction of the computed interval.
    /// 0.20 = ±20% random jitter to prevent thundering herd on multi-node error recovery.
    /// </summary>
    public double ErrorJitterFraction { get; init; } = 0.20;

    /// <summary>Number of consecutive busy cycles before interval is reduced to MinIntervalSeconds.</summary>
    public int BusyThresholdCycles { get; init; } = 3;

    /// <summary>Number of consecutive idle cycles before backoff begins increasing.</summary>
    public int IdleThresholdCycles { get; init; } = 2;

    /// <summary>
    /// Cache entry sliding expiry. Nodes inactive beyond this window are evicted
    /// and start from BaseIntervalSeconds on next access.
    /// </summary>
    public int ActivityWindowMinutes { get; init; } = 60;
}
