namespace MSOSync.Api.Dtos;

/// <summary>Response shape for GET /api/v1/system/scheduler-status.</summary>
public sealed record SchedulerStatusDto(
    string              InstanceId,
    SchedulerJobDto[]   Jobs);

public sealed record SchedulerJobDto(
    string           JobName,
    string           Mode,          // "Idle" | "Running" | "Standby"
    string?          LockOwner,
    DateTimeOffset?  LockedSince,
    DateTimeOffset   LastUpdated);
