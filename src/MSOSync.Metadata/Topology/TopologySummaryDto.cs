namespace MSOSync.Metadata.Topology;

public sealed record TopologySummaryDto(
    int      TotalGroups,
    int      TotalNodes,
    int      ReachableNodes,
    int      DegradedNodes,
    int      UnreachableNodes,
    int      UnknownNodes,
    DateTime GeneratedAt);
