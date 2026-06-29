using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed record TopologyGraphDto(
    IReadOnlyList<TopologyNodeDto> Nodes,
    IReadOnlyList<TopologyEdgeDto> Edges,
    TopologyMetadataDto            Metadata);

public sealed record TopologyNodeDto(
    string             GroupId,
    string?            Name,
    int                TotalNodes,
    int                ReachableNodes,
    int                DegradedNodes,
    int                UnreachableNodes,
    int                UnknownNodes,
    ConnectivityStatus ConnectivityStatus);

public sealed record TopologyEdgeDto(
    string                RouterId,
    string                SourceGroupId,
    string                TargetGroupId,
    IReadOnlyList<string> ChannelIds,
    bool                  Enabled);

public sealed record TopologyMetadataDto(
    string   LayoutHint,
    string   Direction,
    int      Version,
    DateTime GeneratedAt);
