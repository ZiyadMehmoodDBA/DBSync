using MSOSync.Common.Exceptions;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// <summary>
/// Pure domain object — no DB, no services, no logging (spec §2.5).
/// This table is the SINGLE CANONICAL AUTHORITY for transitions (spec §2.2).
/// </summary>
public sealed class NodeLifecycleStateMachine : INodeLifecycleStateMachine
{
    private static readonly IReadOnlyDictionary<NodeLifecycleState, NodeLifecycleState[]> Transitions =
        new Dictionary<NodeLifecycleState, NodeLifecycleState[]>
        {
            [NodeLifecycleState.PendingApproval] =
                [NodeLifecycleState.PendingRegistration, NodeLifecycleState.Rejected, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.PendingRegistration] =
                [NodeLifecycleState.Active, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Active] =
                [NodeLifecycleState.Disabled, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Recovery] =
                [NodeLifecycleState.Active, NodeLifecycleState.Disabled, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Disabled] =
                [NodeLifecycleState.Active, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning],
            [NodeLifecycleState.Decommissioning] =
                [NodeLifecycleState.Decommissioned],
            [NodeLifecycleState.Decommissioned] = [],   // terminal — Invariant 1
            [NodeLifecycleState.Rejected] = [],          // terminal — Invariant 1
        };

    public bool CanTransition(NodeLifecycleState from, NodeLifecycleState to)
        => Transitions[from].Contains(to);

    public IReadOnlyList<NodeLifecycleState> AllowedTargets(NodeLifecycleState from)
        => Transitions[from];

    public void Validate(NodeLifecycleState from, NodeLifecycleState to, Guid correlationId = default)
    {
        if (!CanTransition(from, to))
            throw new InvalidLifecycleTransitionException(
                from.ToString(), to.ToString(),
                Transitions[from].Select(t => t.ToString()).ToArray(),
                correlationId);
    }
}
