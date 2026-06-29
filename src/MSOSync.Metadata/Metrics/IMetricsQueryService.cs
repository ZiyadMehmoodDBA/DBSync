namespace MSOSync.Metadata.Metrics;

public interface IMetricsQueryService
{
    Task<MetricsSummaryDto>                GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<NodeMetricsDto>>    GetNodeMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<ChannelMetricsDto>> GetChannelMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<RuntimeMetricsDto>> GetRuntimeMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<MonitorMetricDto>>  GetMonitorMetricsAsync(string? nodeId, string? metricName, CancellationToken ct);
}
