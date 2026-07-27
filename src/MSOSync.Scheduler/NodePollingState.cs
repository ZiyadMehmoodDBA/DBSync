namespace MSOSync.Scheduler;

/// <summary>
/// Per-node adaptive polling state stored in IMemoryCache.
/// Ephemeral — resets on process restart by design.
/// </summary>
internal sealed record NodePollingState(
    int            ConsecutiveBusyCycles,
    int            ConsecutiveIdleCycles,
    int            ConsecutiveErrorCycles,
    bool           InErrorBackoff,
    DateTimeOffset LastActivity)
{
    /// <summary>Initial state for a node with no history.</summary>
    public static NodePollingState Initial { get; } =
        new(0, 0, 0, false, DateTimeOffset.MinValue);
}
