namespace MSOSync.Metadata.Lifecycle;

public sealed class LifecycleOptions
{
    public const string Section = "Lifecycle";
    public int DecommissionGraceMinutes { get; init; } = 60;
    public int BootstrapTokenTtlHours { get; init; } = 72;
    public bool MaintenanceContinueProbing { get; init; } = true;
    public int ConnectivityHistoryRetentionDays { get; init; } = 30;
    public int ConnectivityEvaluatorIntervalSeconds { get; init; } = 30;
    public int DecommissionWorkerIntervalSeconds { get; init; } = 30;
}
