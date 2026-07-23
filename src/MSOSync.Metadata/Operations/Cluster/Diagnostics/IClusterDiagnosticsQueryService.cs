using MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;

namespace MSOSync.Metadata.Operations.Cluster.Diagnostics;

public interface IClusterDiagnosticsQueryService
{
    Task<ClusterDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct);
}
