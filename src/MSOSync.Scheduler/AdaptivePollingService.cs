using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Scheduler.Options;

namespace MSOSync.Scheduler;

/// <summary>
/// Computes adaptive per-node poll intervals using exponential backoff for idle and error paths.
/// State is stored in IMemoryCache (ephemeral — resets on restart).
/// Registered as singleton.
/// </summary>
public sealed class AdaptivePollingService(
    IMemoryCache                     cache,
    IOptions<AdaptivePollingOptions> options,
    IMetricsService                  metrics) : IAdaptivePollingService
{
    private static string CacheKey(string nodeId) => $"adaptive-polling:{nodeId}";

    public Task<TimeSpan> GetIntervalAsync(string nodeId, CancellationToken ct = default)
    {
        var opts     = options.Value;
        var state    = cache.Get<NodePollingState>(CacheKey(nodeId)) ?? NodePollingState.Initial;
        var interval = ComputeInterval(state, opts);

        // Determine which path was taken for the metrics tag
        var stateName = state.InErrorBackoff ? "error"
            : state.ConsecutiveBusyCycles >= opts.BusyThresholdCycles ? "busy"
            : state.ConsecutiveIdleCycles >= opts.IdleThresholdCycles  ? "idle"
            : "default";

        metrics.RecordHistogram(
            "sync.adaptive.interval_s",
            interval.TotalSeconds,
            new Dictionary<string, string> { ["node_id"] = nodeId, ["state"] = stateName });

        return Task.FromResult(interval);
    }

    public Task RecordActivityAsync(string nodeId, bool hadWork, CancellationToken ct = default)
    {
        var opts  = options.Value;
        var state = cache.Get<NodePollingState>(CacheKey(nodeId)) ?? NodePollingState.Initial;

        var newState = hadWork
            ? state with
            {
                ConsecutiveBusyCycles  = state.ConsecutiveBusyCycles + 1,
                ConsecutiveIdleCycles  = 0,
                ConsecutiveErrorCycles = 0,
                InErrorBackoff         = false,
                LastActivity           = DateTimeOffset.UtcNow
            }
            : state with
            {
                ConsecutiveBusyCycles  = 0,
                ConsecutiveIdleCycles  = state.ConsecutiveIdleCycles + 1,
                ConsecutiveErrorCycles = 0,
                InErrorBackoff         = false
            };

        SetState(nodeId, newState, opts);
        return Task.CompletedTask;
    }

    public Task RecordErrorAsync(string nodeId, CancellationToken ct = default)
    {
        var opts  = options.Value;
        var state = cache.Get<NodePollingState>(CacheKey(nodeId)) ?? NodePollingState.Initial;

        var newState = state with
        {
            ConsecutiveBusyCycles  = 0,
            ConsecutiveIdleCycles  = 0,
            ConsecutiveErrorCycles = state.ConsecutiveErrorCycles + 1,
            InErrorBackoff         = true
        };

        SetState(nodeId, newState, opts);
        return Task.CompletedTask;
    }

    public Task ResetAsync(string nodeId, CancellationToken ct = default)
    {
        cache.Remove(CacheKey(nodeId));
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Core algorithm — matches spec pseudocode exactly
    // -----------------------------------------------------------------------

    private static TimeSpan ComputeInterval(NodePollingState state, AdaptivePollingOptions opts)
    {
        // --- Error backoff path (highest priority) ---
        if (state.InErrorBackoff)
        {
            var errorCount  = Math.Clamp(state.ConsecutiveErrorCycles, 1, opts.MaxErrorBackoffCount);
            var rawSeconds  = opts.BaseIntervalSeconds * Math.Pow(opts.ErrorBackoffMultiplier, errorCount);
            var capped      = Math.Min(rawSeconds, opts.MaxIntervalSeconds);
            var jitterRange = capped * opts.ErrorJitterFraction;
            var jitter      = (Random.Shared.NextDouble() * 2.0 - 1.0) * jitterRange; // uniform in [-jitterRange, +jitterRange]
            var finalSeconds = Math.Clamp(capped + jitter, opts.MinIntervalSeconds, opts.MaxIntervalSeconds);
            return TimeSpan.FromSeconds(finalSeconds);
        }

        // --- Busy path ---
        if (state.ConsecutiveBusyCycles >= opts.BusyThresholdCycles)
            return TimeSpan.FromSeconds(opts.MinIntervalSeconds);

        // --- Idle backoff path ---
        if (state.ConsecutiveIdleCycles >= opts.IdleThresholdCycles)
        {
            var idleCount   = state.ConsecutiveIdleCycles - opts.IdleThresholdCycles + 1;
            var rawSeconds  = opts.BaseIntervalSeconds * Math.Pow(opts.BackoffMultiplier, idleCount);
            return TimeSpan.FromSeconds(Math.Min(rawSeconds, opts.MaxIntervalSeconds));
        }

        // --- Default: no clear signal ---
        return TimeSpan.FromSeconds(opts.BaseIntervalSeconds);
    }

    private void SetState(string nodeId, NodePollingState state, AdaptivePollingOptions opts)
        => cache.Set(
            CacheKey(nodeId),
            state,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(opts.ActivityWindowMinutes)
            });
}
