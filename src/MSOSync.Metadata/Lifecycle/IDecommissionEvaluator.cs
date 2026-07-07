using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public enum DecommissionDecisionReason { DrainCompleted, GraceExpired, OpenBatches }

public sealed record DecommissionDecision(bool Finalize, DecommissionDecisionReason Reason);

public interface IDecommissionEvaluator
{
    Task<DecommissionDecision> EvaluateAsync(SyncNode node, CancellationToken ct = default);
}
