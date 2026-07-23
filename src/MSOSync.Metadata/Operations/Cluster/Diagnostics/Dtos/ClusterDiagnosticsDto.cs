namespace MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;

public sealed record ClusterDiagnosticsDto(
    IReadOnlyList<RuntimeStatsDto>   RuntimeStats,
    IReadOnlyList<ActiveLockDto>     ActiveLocks,
    IReadOnlyList<SlowOperationDto>  SlowOperations);

public sealed record RuntimeStatsDto(
    long      StatId,
    double?   HeapUsedMb,
    double?   HeapMaxMb,
    double?   CpuPercent,
    int?      ThreadCount,
    long?     GcCount,
    double?   UptimeHours,
    DateTime  CapturedAt);

public sealed record ActiveLockDto(
    string LockName,
    string LockOwner,
    double AgeSeconds,
    bool   IsStale);

public sealed record SlowOperationDto(
    Guid    OperationId,
    string  OperationType,
    string  Status,
    double  DurationMinutes,
    int?    ProgressPercent);
