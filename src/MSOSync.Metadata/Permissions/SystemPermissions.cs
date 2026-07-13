namespace MSOSync.Metadata.Permissions;

public static class SystemPermissions
{
    // Named in Epic 9+ policy configuration
    public const string ViewEvents           = "VIEW_EVENTS";
    public const string ViewMetrics          = "VIEW_METRICS";
    public const string ViewAudit            = "VIEW_AUDIT";
    public const string RetryBatches         = "RETRY_BATCHES";
    public const string ReleaseLocks         = "RELEASE_LOCKS";
    public const string EditParameters       = "EDIT_PARAMETERS";
    public const string ManageTriggers       = "MANAGE_TRIGGERS";
    public const string ManageRouters        = "MANAGE_ROUTERS";

    // Named in Epic 10C+
    public const string ManageUsers          = "MANAGE_USERS";
    public const string ExportData           = "EXPORT_DATA";
    public const string ViewTopology         = "VIEW_TOPOLOGY";
    public const string ApproveNodes         = "APPROVE_NODES";

    // Named in Epic 12A–12B
    public const string ProvisionNodes       = "PROVISION_NODES";
    public const string ManageNodeLifecycle  = "MANAGE_NODE_LIFECYCLE";
    public const string ManageConfigurations = "MANAGE_CONFIGURATIONS";

    // Default permission sets per role — used by ResetRoleToDefaultsAsync
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["VIEWER"]   = [ViewEvents, ViewMetrics, ViewAudit, ViewTopology],
            ["OPERATOR"] = [ViewEvents, ViewMetrics, ViewAudit, ViewTopology,
                            ExportData, RetryBatches, ApproveNodes, ReleaseLocks,
                            EditParameters, ManageTriggers, ManageRouters,
                            ManageNodeLifecycle],
            ["ADMIN"]    = [ViewEvents, ViewMetrics, ViewAudit, ViewTopology,
                            ExportData, RetryBatches, ApproveNodes, ReleaseLocks,
                            EditParameters, ManageTriggers, ManageRouters, ManageUsers,
                            ProvisionNodes, ManageNodeLifecycle, ManageConfigurations],
        };
}
