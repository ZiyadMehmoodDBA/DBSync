namespace MSOSync.Scheduler;

/// <summary>
/// Maintains per-node polling state and computes the next poll interval.
/// State is ephemeral (IMemoryCache) — resets on process restart.
/// Registered as singleton.
/// </summary>
public interface IAdaptivePollingService
{
    /// <summary>
    /// Returns the interval to wait before the next poll cycle for this node.
    /// Callers await this duration before dispatching the next SyncJob tick.
    /// </summary>
    Task<TimeSpan> GetIntervalAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Records the outcome of a completed poll cycle.
    /// hadWork = true  → events were found and dispatched this cycle.
    /// hadWork = false → no events found (idle cycle).
    /// </summary>
    Task RecordActivityAsync(string nodeId, bool hadWork, CancellationToken ct = default);

    /// <summary>
    /// Records that a poll cycle ended in an error (exception or transport failure).
    /// Triggers exponential backoff with jitter for subsequent intervals.
    /// </summary>
    Task RecordErrorAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Resets all state for a node. Called when a node is re-activated or
    /// transitions out of an error lifecycle state.
    /// </summary>
    Task ResetAsync(string nodeId, CancellationToken ct = default);
}
