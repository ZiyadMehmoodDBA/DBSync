namespace MSOSync.Metadata.Dtos;

public sealed record NodeDto(
    string NodeId,
    string GroupId,
    string SyncUrl,
    string Status,
    DateTime? RegistrationTime,
    DateTime? LastHeartbeat,
    int HeartbeatInterval,
    bool SyncEnabled);
