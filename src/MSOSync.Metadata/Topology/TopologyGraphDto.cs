using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed record TopologyGraphDto(
    IReadOnlyList<TopologyGraphNodeDto> Nodes,
    IReadOnlyList<TopologyGraphEdgeDto> Edges,
    TopologyGraphMetaDto                Meta);

public sealed record TopologyGraphNodeDto(
    string             Id,           // "group:{groupId}"
    string             GroupId,
    string             Label,
    ConnectivityStatus Status,
    int                MemberCount,
    int                TriggerCount,
    int                ChannelCount);

public sealed record TopologyGraphEdgeDto(
    string                Id,        // "router:{routerId}"
    string                Source,    // "group:{sourceNodeGroup}"
    string                Target,    // "group:{targetNodeGroup}"
    IReadOnlyList<string> ChannelIds,
    bool                  IsEnabled);

public sealed record TopologyGraphMetaDto(
    int            TotalGroups,
    int            TotalNodes,
    int            OnlineNodes,
    DateTimeOffset GeneratedAt);
