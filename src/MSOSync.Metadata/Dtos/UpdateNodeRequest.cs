namespace MSOSync.Metadata.Dtos;

public sealed record UpdateNodeRequest(
    string GroupId,
    string SyncUrl,
    int HeartbeatInterval);
