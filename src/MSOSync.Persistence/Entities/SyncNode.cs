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
    public TransportMode TransportMode { get; set; } = TransportMode.Pull;
    public string? UpstreamNodeId { get; set; }
    public DateTime? LastProbeTime { get; set; }
    public int? LastProbeLatencyMs { get; set; }
    public ConnectivityStatus ConnectivityStatus { get; set; } = ConnectivityStatus.Unknown;

    // DB connection fields (admin-provisioned)
    public string? DbServer { get; set; }
    public string? DbName { get; set; }
    public string? DbAuthMode { get; set; }  // "Windows" or "Sql"
    public string? DbUser { get; set; }
    public string? DbPasswordEncrypted { get; set; }
}
