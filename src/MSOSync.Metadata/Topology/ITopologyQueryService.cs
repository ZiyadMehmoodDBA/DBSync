namespace MSOSync.Metadata.Topology;

public interface ITopologyQueryService
{
    Task<TopologyGraphDto>                    GetTopologyGraphAsync(CancellationToken ct);
    Task<TopologySummaryDto>                  GetTopologySummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupDto>>     GetGroupsAsync(CancellationToken ct);
    Task<TopologyGroupDto?>                   GetGroupAsync(string groupId, CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupNodeDto>> GetGroupNodesAsync(string groupId, CancellationToken ct);
}
