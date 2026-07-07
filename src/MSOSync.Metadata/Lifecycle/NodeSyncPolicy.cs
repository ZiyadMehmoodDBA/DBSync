using System.Linq.Expressions;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed class NodeSyncPolicy : INodeSyncPolicy
{
    /// EF-translatable single source of eligibility for use inside IQueryable.Where.
    public static readonly Expression<Func<SyncNode, bool>> EligibleExpression =
        n => n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode;

    private static readonly Func<SyncNode, bool> Eligible = EligibleExpression.Compile();

    public bool CanSynchronize(SyncNode node) => Eligible(node);

    public SyncEligibility Evaluate(SyncNode node) => node.LifecycleState switch
    {
        NodeLifecycleState.Decommissioning or NodeLifecycleState.Decommissioned
            => SyncEligibility.BlockedByDecommission,
        not NodeLifecycleState.Active => SyncEligibility.BlockedByLifecycle,
        _ when node.MaintenanceMode => SyncEligibility.BlockedByMaintenance,
        _ => SyncEligibility.Allowed,
    };
}
