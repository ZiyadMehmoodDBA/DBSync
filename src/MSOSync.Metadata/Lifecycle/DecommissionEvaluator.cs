using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed class DecommissionEvaluator(AppDbContext db) : IDecommissionEvaluator
{
    public async Task<DecommissionDecision> EvaluateAsync(SyncNode node, CancellationToken ct = default)
    {
        var open = await NodeLifecycleHistoryService.CountOpenBatchesAsync(db, node.NodeId, ct);
        return Decide(open, node.DecommissionGraceUntil, DateTimeOffset.UtcNow);
    }

    /// Pure decision core (unit-tested).
    public static DecommissionDecision Decide(int openBatches, DateTimeOffset? graceUntil, DateTimeOffset now)
    {
        if (openBatches == 0)
            return new(true, DecommissionDecisionReason.DrainCompleted);
        if (graceUntil is null || now >= graceUntil)
            return new(true, DecommissionDecisionReason.GraceExpired);
        return new(false, DecommissionDecisionReason.OpenBatches);
    }
}
