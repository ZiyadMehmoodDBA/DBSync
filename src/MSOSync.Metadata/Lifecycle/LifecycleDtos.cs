using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed record LifecycleTransitionRecord(
    string NodeId,
    NodeLifecycleState? FromState,
    NodeLifecycleState ToState,
    LifecycleTrigger Trigger,
    string? Reason,
    string Actor,
    Guid CorrelationId,
    string? MetadataJson = null);

public sealed record LifecycleHistoryDto(
    long HistoryId,
    string NodeId,
    NodeLifecycleState? FromState,
    NodeLifecycleState ToState,
    LifecycleTrigger Trigger,
    string? Reason,
    string Actor,
    Guid? CorrelationId,
    string? MetadataJson,
    DateTimeOffset OccurredAt);

public sealed record LifecycleHistoryFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    LifecycleTrigger? Trigger = null,
    int Page = 1,
    int PageSize = 50);

public sealed record NodeStateDto(
    string NodeId,
    NodeLifecycleState LifecycleState,
    ConnectivityStatus ConnectivityStatus,
    string? ConnectivityReason,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastProbeUtc,
    bool MaintenanceMode,
    string? MaintenanceReason,
    DateTimeOffset? MaintenanceUntil,
    bool DecommissionInProgress,
    int? DrainProgressPercent,
    DateTimeOffset? DecommissionGraceUntil);

public sealed record ActivateResultDto(
    string NodeToken,
    int HeartbeatIntervalSeconds,
    int ProbeIntervalSeconds,
    int ConfigurationVersion);
