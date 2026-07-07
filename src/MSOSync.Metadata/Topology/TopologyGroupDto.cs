using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

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
    NodeLifecycleState LifecycleState,
    ConnectivityStatus ConnectivityStatus,
    DateTime?          LastHeartbeat,
    int?               LastProbeLatencyMs,
    bool               CanSynchronize);
