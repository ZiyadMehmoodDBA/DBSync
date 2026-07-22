using MSOSync.Metadata.Operations.Cluster.Dtos;

namespace MSOSync.Metadata.Operations.Cluster;

public interface IClusterSummaryQueryService
{
    Task<ClusterSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}
