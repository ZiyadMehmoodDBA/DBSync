using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed record ConnectivityTelemetry(
    NodeLifecycleState Lifecycle,
    DateTime? LastHeartbeatUtc,
    DateTime? LastProbeUtc,
    bool LastProbeFailed,
    int ConsecutiveProbeFailures,
    DateTime NowUtc,
    TimeSpan HeartbeatInterval,
    TimeSpan ProbeInterval);

public sealed record ConnectivityEvaluationResult(ConnectivityStatus Status, ConnectivityReason Reason);

public interface IConnectivityPolicy
{
    ConnectivityEvaluationResult Evaluate(ConnectivityTelemetry snapshot);
}
