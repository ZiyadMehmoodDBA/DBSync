using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Dtos;

public sealed record NodeDto(
    string NodeId,
    string GroupId,
    string SyncUrl,
    NodeLifecycleState LifecycleState,
    DateTime? RegistrationTime,
    DateTime? LastHeartbeat,
    int HeartbeatInterval,
    bool CanSynchronize,
    TransportMode TransportMode,
    ConnectivityStatus ConnectivityStatus,
    bool MaintenanceMode,
    string? DbServer,
    string? DbName,
    string? DbAuthMode,
    string? DbUser,
    bool HasDbPassword);   // true if password is stored; never expose the encrypted value
