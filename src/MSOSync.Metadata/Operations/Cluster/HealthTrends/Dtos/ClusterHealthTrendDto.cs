namespace MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;

public sealed record ClusterHealthTrendDto(
    string                             Window,
    int                                BucketCount,
    IReadOnlyList<HealthBucketDto>     Buckets,
    IReadOnlyList<NodeProbeStatsDto>   NodeProbeStats);

public sealed record HealthBucketDto(
    DateTime BucketStart,
    int      ReachableCount,
    int      DegradedCount,
    int      UnreachableCount,
    int      TotalNodes,
    int      TransitionCount);

public sealed record NodeProbeStatsDto(
    string NodeId,
    string ConnectivityStatus,
    int?   LastProbeLatencyMs,       // Always null — SyncNodeConnectivityHistory has no latency field
    int    ConsecutiveProbeFailures,
    double UptimePct);
