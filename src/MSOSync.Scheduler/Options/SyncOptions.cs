namespace MSOSync.Scheduler;

public sealed class SyncOptions
{
    public const string Section = "Sync";
    public int IntervalSeconds { get; init; } = 30;
    public int PullIntervalSeconds { get; init; } = 10;
}
