namespace MSOSync.Common.Workers;

public enum WorkerState { Running, Idle, Warning, Delayed, Failed, Disabled }
public enum WorkerExecutionState { Running, Idle }
public enum WorkerHealthState { Healthy, Warning, Delayed, Failed, Disabled }
public enum TickTrigger { Scheduled, Manual, Startup, Retry }

public sealed record TickRecord(
    DateTime StartedAt,
    DateTime CompletedAt,
    long DurationMs,
    bool Success,
    string? Error,
    TickTrigger Trigger);

public sealed record WorkerStatusDto(
    string WorkerName,
    string WorkerVersion,
    TimeSpan ExpectedInterval,
    DateTime RegisteredAt,
    bool Enabled,
    WorkerState State,
    WorkerExecutionState ExecutionState,
    WorkerHealthState HealthState,
    DateTime? LastStarted,
    DateTime? LastCompleted,
    DateTime? LastSuccessfulRun,
    DateTime? NextExpected,
    long AverageDurationMs,
    long LastDurationMs,
    long ExecutionCount,
    int ConsecutiveFailures,
    string? LastError,
    DateTime LastHeartbeat,
    double SuccessRatePct,
    long MaxDurationMs,
    int FailureCount,
    DateTime? LastFailureAt,
    TickRecord[] RecentTicks);
