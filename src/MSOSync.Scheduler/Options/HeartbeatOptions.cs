namespace MSOSync.Scheduler;

public sealed class HeartbeatOptions
{
    public const string Section = "Heartbeat";
    public int IntervalSeconds { get; init; } = 30;
    public int ProbeIntervalSeconds { get; init; } = 60;
}
