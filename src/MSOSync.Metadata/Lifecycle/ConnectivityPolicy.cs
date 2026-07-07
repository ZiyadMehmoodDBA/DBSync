using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

/// Deterministic ordered rules — spec §5.2. Pure: no I/O, no clock (Now provided in snapshot).
public sealed class ConnectivityPolicy : IConnectivityPolicy
{
    public ConnectivityEvaluationResult Evaluate(ConnectivityTelemetry s)
    {
        // Rule 1 — excluded lifecycles
        if (s.Lifecycle is NodeLifecycleState.PendingApproval
                        or NodeLifecycleState.PendingRegistration
                        or NodeLifecycleState.Rejected
                        or NodeLifecycleState.Decommissioned)
            return new(ConnectivityStatus.Unknown, ConnectivityReason.NotEvaluated);

        // Rule 2 — no heartbeat ever received
        if (s.LastHeartbeatUtc is null)
            return new(ConnectivityStatus.Unknown, ConnectivityReason.NoHeartbeat);

        var heartbeatAge = s.NowUtc - s.LastHeartbeatUtc.Value;

        // Rule 3 — heartbeat expired
        if (heartbeatAge > 3 * s.HeartbeatInterval)
            return new(ConnectivityStatus.Unreachable, ConnectivityReason.HeartbeatExpired);

        // Rule 4 — heartbeat stale
        if (heartbeatAge > s.HeartbeatInterval)
            return new(ConnectivityStatus.Degraded, ConnectivityReason.HeartbeatStale);

        // Stale probes are ignored (spec §5.2): a just-rebooted healthy node is not
        // downgraded by a pre-reboot probe failure.
        var probeFresh = s.LastProbeUtc is not null
            && (s.NowUtc - s.LastProbeUtc.Value) <= 2 * s.ProbeInterval;

        // Rule 6 — 3+ consecutive fresh probe failures (checked before rule 5: stronger signal)
        if (probeFresh && s.LastProbeFailed && s.ConsecutiveProbeFailures >= 3)
            return new(ConnectivityStatus.Unreachable, ConnectivityReason.ProbeFailures);

        // Rule 5 — fresh probe failure
        if (probeFresh && s.LastProbeFailed)
            return new(ConnectivityStatus.Degraded, ConnectivityReason.ProbeFailed);

        // Rule 7
        return new(ConnectivityStatus.Reachable, ConnectivityReason.Healthy);
    }
}
