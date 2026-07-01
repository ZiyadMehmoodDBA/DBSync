using MSOSync.Persistence;

namespace MSOSync.Metadata.Dtos;

public sealed record NodeDto(
    string NodeId,
    string GroupId,
    string SyncUrl,
    string Status,
    DateTime? RegistrationTime,
    DateTime? LastHeartbeat,
    int HeartbeatInterval,
    bool SyncEnabled,
    TransportMode TransportMode,
    string? DbServer,
    string? DbName,
    string? DbAuthMode,
    string? DbUser,
    bool HasDbPassword);   // true if password is stored; never expose the encrypted value
