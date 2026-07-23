using MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;

namespace MSOSync.Metadata.Operations.Cluster.Recovery;

public interface IRecoveryDashboardQueryService
{
    Task<RecoveryDashboardDto> GetRecoveryDashboardAsync(CancellationToken ct);
}
