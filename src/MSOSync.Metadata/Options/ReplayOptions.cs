namespace MSOSync.Metadata.Options;

public sealed class ReplayOptions
{
    public const string Section = "Replay";

    public int MaxRangeDays            { get; init; } = 90;
    public int WorkerIntervalSeconds   { get; init; } = 10;
    public int MaxConcurrentOperations { get; init; } = 5;
    public int ItemPageSize            { get; init; } = 50;
}
