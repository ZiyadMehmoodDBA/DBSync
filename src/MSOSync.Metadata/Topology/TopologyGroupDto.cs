using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed record TopologyGroupDto(
    string             GroupId,
    string?            Name,
    int                TotalNodes,
    int                ReachableNodes,
    int                DegradedNodes,
    int                UnreachableNodes,
    int                UnknownNodes,
    ConnectivityStatus ConnectivityStatus);

public sealed record TopologyGroupNodeDto(
    string             NodeId,
    NodeStatus         Status,
    ConnectivityStatus ConnectivityStatus,
    DateTime?          LastHeartbeat,
    int?               LastProbeLatencyMs,
    bool               SyncEnabled);
