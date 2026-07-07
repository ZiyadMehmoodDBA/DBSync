namespace MSOSync.Persistence.Entities;

public enum NodeLifecycleState
{
    PendingApproval,      // reachable post-cutover only via migrated legacy PENDING rows
    PendingRegistration,  // SyncNode exists, awaiting /activate handshake
    Active,
    Recovery,             // identity replacement under review / awaiting re-activation
    Disabled,
    Decommissioning,      // orchestrated drain in progress
    Decommissioned,       // terminal
    Rejected,             // terminal
}
