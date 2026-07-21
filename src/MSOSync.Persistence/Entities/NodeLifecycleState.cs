namespace MSOSync.Persistence.Entities;

public enum NodeLifecycleState
{
    PendingApproval,      // reachable post-cutover only via migrated legacy PENDING rows
    PendingRegistration,  // SyncNode exists, awaiting /activate handshake
    Active,
    Recovery,             // identity replacement under review / awaiting re-activation
    Disabled,
    Draining,             // reversible quiesce: routing excluded, in-flight completes, heartbeats accepted
    Decommissioning,      // orchestrated drain in progress
    Decommissioned,       // terminal
    Rejected,             // terminal
}
