namespace MSOSync.Persistence.Entities;

public sealed class SyncNode
{
    public string NodeId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public string SyncUrl { get; set; } = null!;
    public NodeLifecycleState LifecycleState { get; set; } = NodeLifecycleState.PendingRegistration;
    public DateTime? RegistrationTime { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public int HeartbeatInterval { get; set; } = 60;
    public TransportMode TransportMode { get; set; } = TransportMode.Pull;
    public string? UpstreamNodeId { get; set; }
    public DateTime? LastProbeTime { get; set; }
    public int? LastProbeLatencyMs { get; set; }
    public ConnectivityStatus ConnectivityStatus { get; set; } = ConnectivityStatus.Unknown;
    public ConnectivityReason? ConnectivityReason { get; set; }
    public string? LastProbeError { get; set; }
    public int ConsecutiveProbeFailures { get; set; }

    // Recovery
    public NodeLifecycleState? PreviousLifecycleState { get; set; }

    // Maintenance (orthogonal — never a lifecycle state)
    public bool MaintenanceMode { get; set; }
    public string? MaintenanceReason { get; set; }
    public DateTimeOffset? MaintenanceStartedAt { get; set; }
    public DateTimeOffset? MaintenanceUntil { get; set; }
    public string? MaintenanceStartedBy { get; set; }

    // Decommission
    public string? DecommissionReason { get; set; }
    public DateTimeOffset? DecommissionStartedAt { get; set; }
    public DateTimeOffset? DecommissionGraceUntil { get; set; }
    public int? DecommissionInitialOpenBatches { get; set; }

    // Optimistic concurrency for lifecycle commands
    public byte[] RowVersion { get; set; } = [];

    // Node classification fields (admin-provisioned)
    public string NodeType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;

    // DB connection fields (admin-provisioned)
    public string? DbServer { get; set; }
    public string? DbName { get; set; }
    public string? DbAuthMode { get; set; }  // "Windows" or "Sql"
    public string? DbUser { get; set; }
    public string? DbPasswordEncrypted { get; set; }
}
