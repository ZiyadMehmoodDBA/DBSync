namespace MSOSync.Metadata.Dashboard;

public sealed record DashboardSummaryDto(
    int      TotalNodes,
    int      ReachableNodes,
    int      DegradedNodes,
    int      UnreachableNodes,
    int      UnknownNodes,
    long     PendingEvents,
    long     QueueDepth,
    long     EventsToday,
    long     TransportErrors24h,
    DateTime GeneratedAt);
