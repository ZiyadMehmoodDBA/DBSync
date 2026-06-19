namespace MSOSync.Persistence.Entities;

public sealed class SyncNode
{
    public string NodeId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public string SyncUrl { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? RegistrationTime { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public int HeartbeatInterval { get; set; } = 60;
    public bool SyncEnabled { get; set; } = true;
}
