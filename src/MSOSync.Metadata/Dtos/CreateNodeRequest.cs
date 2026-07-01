using MSOSync.Persistence;

namespace MSOSync.Metadata.Dtos;

public sealed record CreateNodeRequest(
    string NodeId,
    string GroupId,
    string SyncUrl,
    int HeartbeatInterval,
    TransportMode TransportMode,
    string? UpstreamNodeId,
    string? DbServer,
    string? DbName,
    string? DbAuthMode,      // "Windows" or "Sql"
    string? DbUser,
    string? DbPassword);     // plaintext — will be encrypted at service layer
