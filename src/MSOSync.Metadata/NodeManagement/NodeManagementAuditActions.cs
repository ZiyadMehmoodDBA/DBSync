namespace MSOSync.Metadata.NodeManagement;

public static class NodeManagementAuditActions
{
    public const string NodeRegistered            = "NODE_REGISTERED";
    public const string NodeApproved              = "NODE_APPROVED";
    public const string NodeRejected              = "NODE_REJECTED";
    public const string NodeReRegistered          = "NODE_RE_REGISTERED";
    public const string ProvisionPackageDownloaded = "PROVISION_PACKAGE_DOWNLOADED";

    // Epic 12B-1 — lifecycle command pipeline
    public const string NodeProvisioned            = "NODE_PROVISIONED";
    public const string NodeActivated              = "NODE_ACTIVATED";
    public const string NodeEnabled                = "NODE_ENABLED";
    public const string NodeDisabled               = "NODE_DISABLED";
    public const string NodeMaintenanceStarted     = "NODE_MAINTENANCE_STARTED";
    public const string NodeMaintenanceExtended    = "NODE_MAINTENANCE_EXTENDED";
    public const string NodeMaintenanceEnded       = "NODE_MAINTENANCE_ENDED";
    public const string NodeDecommissionStarted    = "NODE_DECOMMISSION_STARTED";
    public const string NodeDecommissionCompleted  = "NODE_DECOMMISSION_COMPLETED";
    public const string NodeDecommissionForced     = "NODE_DECOMMISSION_FORCED";
    public const string NodeDecommissionCancelled  = "NODE_DECOMMISSION_CANCELLED"; // reserved — not used in 12B-1
    public const string NodeRecoveryRequested      = "NODE_RECOVERY_REQUESTED";
    public const string NodeRecoveryApproved       = "NODE_RECOVERY_APPROVED";
    public const string NodeRecoveryRejected       = "NODE_RECOVERY_REJECTED";
    public const string NodeRecoveryActivated      = "NODE_RECOVERY_ACTIVATED";

    // Phase 2B.1 — Drain
    public const string NodeDrainStarted   = "NODE_DRAIN_STARTED";
    public const string NodeDrainResumed   = "NODE_DRAIN_RESUMED";
    public const string NodeDrainCompleted = "NODE_DRAIN_COMPLETED";

    // Epic 12C — Sync Scope
    public const string NodeScopeUpdated = "NODE_SCOPE_UPDATED";
}
