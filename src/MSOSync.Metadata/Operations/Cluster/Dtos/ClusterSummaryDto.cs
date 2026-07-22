namespace MSOSync.Metadata.Operations.Cluster.Dtos;

public sealed record ClusterSummaryDto(
    NodeStateCountsDto                       NodeStates,
    OperationCountsDto                       OperationCounts,
    IReadOnlyList<ActiveOperationSummaryDto> ActiveOperations,
    IReadOnlyList<RollingWaveSummaryDto>     ActiveRollingOps,
    IReadOnlyList<ReplayOperationSummaryDto> ActiveReplays,
    IReadOnlyList<NodeStateChangeDto>        RecentNodeChanges);

public sealed record NodeStateCountsDto(
    int Total, int Active, int Maintenance, int Draining, int Offline);

public sealed record OperationCountsDto(
    int Running, int Pending, int SucceededToday, int FailedToday);

public sealed record ActiveOperationSummaryDto(
    Guid     OperationId,
    string   Type,
    string   Status,
    string?  NodeId,
    int?     ProgressPercent,
    string?  ProgressMessage,
    DateTime StartedAt);

public sealed record RollingWaveSummaryDto(
    Guid   OperationId,
    string Mode,        // "RollingMaintenance" | "RollingUpgrade"
    string Status,
    int    CurrentWave,
    int    TotalWaves,
    int    NodesDone,
    int    NodesTotal,
    int    NodesFailed);

public sealed record ReplayOperationSummaryDto(
    Guid   OperationId,
    string ReplayMode,
    string Status,
    int    ItemsDone,
    int    ItemsTotal,
    int    ItemsFailed);

public sealed record NodeStateChangeDto(
    string        NodeId,
    string?       FromState,
    string        ToState,
    string        Trigger,
    DateTimeOffset OccurredAt);
