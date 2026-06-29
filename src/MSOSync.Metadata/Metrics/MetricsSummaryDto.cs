namespace MSOSync.Metadata.Metrics;

public sealed record MetricsSummaryDto(
    int      TotalNodes,
    int      ReachableNodes,
    int      DegradedNodes,
    int      UnreachableNodes,
    int      UnknownNodes,
    long     IncomingQueueDepth,
    long     OutgoingQueueDepth,
    long     BatchesProcessed24h,
    long     Errors24h,
    double   ErrorRatePercent,
    double   ThroughputPerMinute,
    DateTime GeneratedAt);
