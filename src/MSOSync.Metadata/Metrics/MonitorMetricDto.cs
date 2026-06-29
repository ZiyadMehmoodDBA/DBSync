namespace MSOSync.Metadata.Metrics;

public sealed record MonitorMetricDto(
    string?  NodeId,
    string?  MetricName,
    string?  MetricValue,
    DateTime CreateTime);
