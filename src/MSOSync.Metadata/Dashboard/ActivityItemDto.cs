namespace MSOSync.Metadata.Dashboard;

public sealed record ActivityItemDto(
    string   Type,
    DateTime Timestamp,
    string?  NodeId,
    string   Summary,
    string?  Detail);
