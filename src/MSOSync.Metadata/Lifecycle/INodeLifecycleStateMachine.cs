using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public interface INodeLifecycleStateMachine
{
    bool CanTransition(NodeLifecycleState from, NodeLifecycleState to);
    IReadOnlyList<NodeLifecycleState> AllowedTargets(NodeLifecycleState from);

    /// <summary>Throws <see cref="MSOSync.Common.Exceptions.InvalidLifecycleTransitionException"/> when denied.</summary>
    void Validate(NodeLifecycleState from, NodeLifecycleState to, Guid correlationId = default);
}
