using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;

namespace MSOSync.Metadata.Operations.Cluster.HealthTrends;

public interface IClusterHealthTrendService
{
    Task<ClusterHealthTrendDto> GetTrendsAsync(string window, string? nodeId, CancellationToken ct);
}
