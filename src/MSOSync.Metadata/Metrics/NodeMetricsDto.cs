using MSOSync.Persistence;

namespace MSOSync.Metadata.Metrics;

public sealed record NodeMetricsDto(
    string             NodeId,
    string             GroupId,
    ConnectivityStatus ConnectivityStatus,
    long               IncomingQueueDepth,
    long               OutgoingQueueDepth,
    int                ProcessedBatches24h,
    int                Errors24h,
    double?            AvgApplyTimeMs,
    DateTime?          LastHeartbeat);
