using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence;

/// Single source for the M022 legacy status conversion (spec §3.1).
public static class LegacyStatusMap
{
    public static readonly IReadOnlyDictionary<string, NodeLifecycleState> Map =
        new Dictionary<string, NodeLifecycleState>(StringComparer.OrdinalIgnoreCase)
        {
            ["PENDING"]     = NodeLifecycleState.PendingApproval,
            ["APPROVED"]    = NodeLifecycleState.PendingRegistration,
            ["PROVISIONED"] = NodeLifecycleState.PendingRegistration,
            ["REGISTERED"]  = NodeLifecycleState.Active,
            ["OFFLINE"]     = NodeLifecycleState.Active,
            ["DISABLED"]    = NodeLifecycleState.Disabled,
        };
}
