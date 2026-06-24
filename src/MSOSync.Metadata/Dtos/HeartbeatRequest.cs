namespace MSOSync.Metadata.Dtos;

public sealed record HeartbeatRequest(
    string  NodeId,
    string? NodeVersion,
    long    UptimeSeconds,
    string? DatabaseType,
    string? TransportMode);
