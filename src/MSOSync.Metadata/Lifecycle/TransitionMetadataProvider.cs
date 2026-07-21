using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// Backend owns the workflow contract (spec §7.3): the frontend renders exactly what
/// this returns and encodes ZERO transition rules of its own.
public sealed class TransitionMetadataProvider(INodeLifecycleStateMachine stateMachine)
    : ITransitionMetadataProvider
{
    public TransitionsDto GetTransitions(SyncNode node)
    {
        var actions = new List<TransitionActionDto>();

        foreach (var target in stateMachine.AllowedTargets(node.LifecycleState))
        {
            switch (target)
            {
                case NodeLifecycleState.Active when node.LifecycleState == NodeLifecycleState.Disabled:
                    actions.Add(new("Enable", false, true, "Normal"));
                    break;
                case NodeLifecycleState.Active when node.LifecycleState == NodeLifecycleState.Draining:
                    actions.Add(new("ResumeDrain", false, false, "Normal"));
                    break;
                case NodeLifecycleState.Disabled when node.LifecycleState == NodeLifecycleState.Active:
                    actions.Add(new("Disable", false, true, "Normal"));
                    break;
                case NodeLifecycleState.Draining when node.LifecycleState == NodeLifecycleState.Active:
                    actions.Add(new("StartDrain", false, true, "Normal"));
                    break;
                case NodeLifecycleState.Decommissioning:
                    actions.Add(new("Decommission", true, true, "Critical"));
                    break;
                case NodeLifecycleState.Decommissioned:
                    actions.Add(new("ForceCompleteDecommission", false, true, "Critical"));
                    break;
                // Recovery entry is registration-driven; Active-via-activation is node-driven;
                // Rejected is registration-reject; none are operator grid actions.
            }
        }

        // Maintenance is not a transition (spec §4.3) but IS an allowed action (spec §7.3)
        if (node.LifecycleState == NodeLifecycleState.Active)
        {
            actions.Add(node.MaintenanceMode
                ? new("EndMaintenance", false, false, "Normal")
                : new("StartMaintenance", true, false, "Normal"));
        }

        return new TransitionsDto(node.LifecycleState.ToString(), actions);
    }
}
