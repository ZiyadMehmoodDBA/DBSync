using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public enum SyncEligibility
{
    Allowed,
    BlockedByLifecycle,
    BlockedByMaintenance,
    BlockedByDecommission,
    BlockedByPolicy,
}

public interface INodeSyncPolicy
{
    bool CanSynchronize(SyncNode node);
    SyncEligibility Evaluate(SyncNode node);
}
