namespace MSOSync.Metadata.Overview;

public sealed record OverviewDto(
    OverviewHealthWidget Health,
    OverviewOperationsWidget Operations,
    OverviewNodesWidget Nodes,
    OverviewConfigurationWidget Configuration,
    OverviewWarningDto[] Warnings,
    OverviewEventDto[] RecentActivity,
    OverviewSystemWidget System,
    DateTime LastRefreshedAt);

public sealed record OverviewHealthWidget(
    string ClusterHealth,
    string WorkerHealth,
    string NodeHealth);

public sealed record OverviewOperationsWidget(
    int Running,
    int SucceededToday,
    int FailedToday,
    int Queued);

public sealed record OverviewNodesWidget(
    int Total,
    int Active,
    int Offline,
    int Maintenance,
    int Degraded,
    int PendingRegistrations);

public sealed record OverviewConfigurationWidget(
    int DriftedCount,
    int UpdateAvailableCount,
    int FailedCount);

public sealed record OverviewWarningDto(
    string Type,
    string Severity,
    string Title,
    string Description,
    string TargetRoute,
    string? CorrelationId);

public sealed record OverviewEventDto(
    string EventId,
    DateTime OccurredAt,
    string Category,
    string Summary,
    string? NodeId,
    string? CorrelationId,
    string? DeepLink);

public sealed record OverviewSystemWidget(
    string Version,
    string DatabaseMigration,
    string Environment,
    string Uptime,
    string SignalRStatus,
    DateTime LastRefreshedAt);

public sealed record SystemInfoDto(
    string Version,
    string BuildDate,
    string GitCommit,
    string DotNetRuntime,
    string OperatingSystem,
    string DatabaseMigration,
    string Edition,
    string Environment,
    string ServerTime,
    string ProcessUptime);
