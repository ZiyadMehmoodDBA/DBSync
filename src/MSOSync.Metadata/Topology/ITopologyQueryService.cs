using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.Topology;

public interface ITopologyQueryService
{
    /// <summary>Full graph, no filter.</summary>
    Task<TopologyGraphDto> GetTopologyGraphAsync(CancellationToken ct)
        => GetTopologyGraphAsync(null, ct);

    /// <summary>Graph optionally filtered to groups containing any of the given node IDs.</summary>
    Task<TopologyGraphDto> GetTopologyGraphAsync(string[]? nodeIdFilter, CancellationToken ct);

    Task<TopologySummaryDto>              GetTopologySummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupDto>> GetGroupsAsync(CancellationToken ct);
    Task<TopologyGroupDto?>               GetGroupAsync(string groupId, CancellationToken ct);

    /// <summary>Cursor-paginated group membership. pageSize max 500.</summary>
    Task<CursorPageResult<TopologyGroupNodeDto>> GetGroupNodesAsync(
        string groupId, string? cursor, int pageSize, CancellationToken ct);
}
