namespace MSOSync.Metadata.Metrics;

public sealed record ChannelMetricsDto(
    string  ChannelId,
    int     ActiveNodes,
    long    PendingEvents,
    long    PendingOutgoingBatches,
    long    ProcessedBatches24h,
    int     Errors24h,
    double  ThroughputPerMinute);
