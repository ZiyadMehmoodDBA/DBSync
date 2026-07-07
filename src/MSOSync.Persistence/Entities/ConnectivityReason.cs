namespace MSOSync.Persistence.Entities;

public enum ConnectivityReason
{
    NotEvaluated,
    NoHeartbeat,
    Healthy,
    HeartbeatStale,
    HeartbeatExpired,
    ProbeFailed,
    ProbeFailures,
    PendingActivation,
}
