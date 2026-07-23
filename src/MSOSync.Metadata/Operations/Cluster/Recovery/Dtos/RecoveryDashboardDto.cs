namespace MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;

public sealed record RecoveryDashboardDto(
    RecoverySummaryDto                   Summary,
    IReadOnlyList<ActiveRecoveryDto>     ActiveRecoveries,
    IReadOnlyList<CompletedRecoveryDto>  RecentCompletedRecoveries);

public sealed record RecoverySummaryDto(
    int     ActiveCount,
    double? AvgRtoMinutes,
    double? MaxRtoMinutes,
    int     CompletedLast30Days);

public sealed record ActiveRecoveryDto(
    string                        NodeId,
    DateTime?                     FailureDetectedAt,
    DateTime                      RecoveryStartedAt,
    double                        ElapsedMinutes,
    IReadOnlyList<ReplayOpRefDto> AssociatedReplayOps);

public sealed record CompletedRecoveryDto(
    string    NodeId,
    DateTime? FailureDetectedAt,
    DateTime  RecoveryStartedAt,
    DateTime  RestoredAt,
    double    RtoMinutes);

public sealed record ReplayOpRefDto(
    Guid   OperationId,
    string Status,
    int    ItemsDone,
    int    ItemsTotal);
