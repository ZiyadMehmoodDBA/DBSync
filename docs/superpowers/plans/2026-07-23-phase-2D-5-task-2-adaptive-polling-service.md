# Task 2: `IAdaptivePollingService` + `AdaptivePollingService` + Unit Tests

> Part of [Phase 2D.5 Master Plan](2026-07-23-phase-2D-5-master.md)

**Goal:** Implement the adaptive interval algorithm: `NodePollingState` record, `AdaptivePollingOptions`, `IAdaptivePollingService` interface, and `AdaptivePollingService` backed by `IMemoryCache`. Fully unit-tested against the spec's convergence guarantees.

**Files:**
- Create: `src/MSOSync.Scheduler/NodePollingState.cs`
- Create: `src/MSOSync.Scheduler/Options/AdaptivePollingOptions.cs`
- Create: `src/MSOSync.Scheduler/IAdaptivePollingService.cs`
- Create: `src/MSOSync.Scheduler/AdaptivePollingService.cs`
- Create: `tests/MSOSync.SchedulerTests/AdaptivePollingServiceTests.cs`

**Interfaces:**
- Consumes (from Task 1): `IMetricsService.RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null)` — used to record `sync.adaptive.interval_s`
- Produces:
  - `IAdaptivePollingService` — `Task<TimeSpan> GetIntervalAsync(string nodeId, CancellationToken ct = default)`, `Task RecordActivityAsync(string nodeId, bool hadWork, CancellationToken ct = default)`, `Task RecordErrorAsync(string nodeId, CancellationToken ct = default)`, `Task ResetAsync(string nodeId, CancellationToken ct = default)`
  - `AdaptivePollingOptions` with `Section = "AdaptivePolling"` and all spec fields

---

- [ ] **Step 1: Add `Microsoft.Extensions.Caching.Memory` to `MSOSync.Scheduler.csproj`**

`AdaptivePollingService` uses `IMemoryCache` directly. The Scheduler project must explicitly reference this package (it currently only receives it transitively via `MSOSync.Metadata`).

Edit `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj` — add inside the first `<ItemGroup>` with `PackageReference`:

```xml
<PackageReference Include="Microsoft.Extensions.Caching.Memory" />
```

Run `dotnet restore src/MSOSync.Scheduler/MSOSync.Scheduler.csproj` to confirm the package resolves.

- [ ] **Step 2: Create `NodePollingState` record**



Create `src/MSOSync.Scheduler/NodePollingState.cs`:

```csharp
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
```

- [ ] **Step 3: Create `AdaptivePollingOptions`**

Create `src/MSOSync.Scheduler/Options/AdaptivePollingOptions.cs`:

```csharp
namespace MSOSync.Scheduler.Options;

public sealed class AdaptivePollingOptions
{
    public const string Section = "AdaptivePolling";

    /// <summary>Floor: fastest poll rate, applied when node is continuously busy.</summary>
    public int MinIntervalSeconds { get; init; } = 5;

    /// <summary>Ceiling: slowest poll rate, applied when node has been idle long enough.</summary>
    public int MaxIntervalSeconds { get; init; } = 300;

    /// <summary>Starting interval for a node with no history.</summary>
    public int BaseIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Multiplier applied per consecutive idle cycle.
    /// Interval grows: base × BackoffMultiplier^idleCount, capped at MaxIntervalSeconds.
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Multiplier applied per consecutive error cycle (independent of idle backoff).
    /// Applied on top of base: base × ErrorBackoffMultiplier^errorCount.
    /// </summary>
    public double ErrorBackoffMultiplier { get; init; } = 2.0;

    /// <summary>Maximum consecutive error backoffs before interval is capped at MaxIntervalSeconds.</summary>
    public int MaxErrorBackoffCount { get; init; } = 5;

    /// <summary>
    /// Jitter range applied to error-backoff intervals as a fraction of the computed interval.
    /// 0.20 = ±20% random jitter to prevent thundering herd on multi-node error recovery.
    /// </summary>
    public double ErrorJitterFraction { get; init; } = 0.20;

    /// <summary>Number of consecutive busy cycles before interval is reduced to MinIntervalSeconds.</summary>
    public int BusyThresholdCycles { get; init; } = 3;

    /// <summary>Number of consecutive idle cycles before backoff begins increasing.</summary>
    public int IdleThresholdCycles { get; init; } = 2;

    /// <summary>
    /// Cache entry sliding expiry. Nodes inactive beyond this window are evicted
    /// and start from BaseIntervalSeconds on next access.
    /// </summary>
    public int ActivityWindowMinutes { get; init; } = 60;
}
```

- [ ] **Step 4: Create `IAdaptivePollingService` interface**

Create `src/MSOSync.Scheduler/IAdaptivePollingService.cs`:

```csharp
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
```

- [ ] **Step 5: Write failing unit tests for `AdaptivePollingService`**

Create `tests/MSOSync.SchedulerTests/AdaptivePollingServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common;
using MSOSync.Scheduler;
using MSOSync.Scheduler.Options;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class AdaptivePollingServiceTests : IDisposable
{
    private readonly IMemoryCache    _cache   = new MemoryCache(new MemoryCacheOptions());
    private readonly IMetricsService _metrics = Mock.Of<IMetricsService>();

    private AdaptivePollingService Build(
        int min        = 5,
        int max        = 300,
        int @base      = 30,
        double backoff = 2.0,
        double errBack = 2.0,
        int maxErr     = 5,
        double jitter  = 0.20,
        int busyThresh = 3,
        int idleThresh = 2,
        int windowMin  = 60)
    {
        var opts = Options.Create(new AdaptivePollingOptions
        {
            MinIntervalSeconds    = min,
            MaxIntervalSeconds    = max,
            BaseIntervalSeconds   = @base,
            BackoffMultiplier     = backoff,
            ErrorBackoffMultiplier = errBack,
            MaxErrorBackoffCount  = maxErr,
            ErrorJitterFraction   = jitter,
            BusyThresholdCycles   = busyThresh,
            IdleThresholdCycles   = idleThresh,
            ActivityWindowMinutes = windowMin
        });
        return new AdaptivePollingService(_cache, opts, _metrics);
    }

    [Fact]
    public async Task GetInterval_NoHistory_ReturnsBaseInterval()
    {
        var svc = Build(@base: 30);
        var interval = await svc.GetIntervalAsync("node-x");
        interval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task BusyConvergence_ThreeConsecutiveBusy_ReturnsMinInterval()
    {
        var svc = Build(min: 5, busyThresh: 3);
        await svc.RecordActivityAsync("node-1", hadWork: true);
        await svc.RecordActivityAsync("node-1", hadWork: true);
        await svc.RecordActivityAsync("node-1", hadWork: true);
        var interval = await svc.GetIntervalAsync("node-1");
        interval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BusyConvergence_TwoBusyNotEnough_ReturnsBaseInterval()
    {
        var svc = Build(@base: 30, min: 5, busyThresh: 3);
        await svc.RecordActivityAsync("node-2", hadWork: true);
        await svc.RecordActivityAsync("node-2", hadWork: true);
        var interval = await svc.GetIntervalAsync("node-2");
        interval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task IdleBackoff_IntervalSequenceMatchesSpec()
    {
        // Spec: base=30, multiplier=2.0, idleThresh=2, max=300
        // Idle cycle 1 → 30 s (below threshold)
        // Idle cycle 2 → 30 s (at threshold, idleCount=2, backoff starts at idleCount - idleThresh + 1 = 1 → 30*2^1=60? No.
        //   Re-read spec: "if ConsecutiveIdleCycles >= IdleThresholdCycles"
        //   idleCount = ConsecutiveIdleCycles - IdleThresholdCycles + 1
        //   At idle=2: idleCount=1, raw=30*2^1=60
        //   At idle=3: idleCount=2, raw=30*2^2=120
        //   At idle=4: idleCount=3, raw=30*2^3=240
        //   At idle=5: idleCount=4, raw=30*2^4=480 → capped at 300
        var svc = Build(@base: 30, max: 300, backoff: 2.0, idleThresh: 2, min: 5);

        // First idle — below threshold (ConsecutiveIdleCycles=1 < 2)
        await svc.RecordActivityAsync("n", hadWork: false);
        (await svc.GetIntervalAsync("n")).Should().Be(TimeSpan.FromSeconds(30));

        // Second idle — at threshold, idleCount=1, raw=60
        await svc.RecordActivityAsync("n", hadWork: false);
        (await svc.GetIntervalAsync("n")).Should().Be(TimeSpan.FromSeconds(60));

        // Third idle — idleCount=2, raw=120
        await svc.RecordActivityAsync("n", hadWork: false);
        (await svc.GetIntervalAsync("n")).Should().Be(TimeSpan.FromSeconds(120));

        // Fourth idle — idleCount=3, raw=240
        await svc.RecordActivityAsync("n", hadWork: false);
        (await svc.GetIntervalAsync("n")).Should().Be(TimeSpan.FromSeconds(240));

        // Fifth idle — idleCount=4, raw=480 → capped at 300
        await svc.RecordActivityAsync("n", hadWork: false);
        (await svc.GetIntervalAsync("n")).Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public async Task IdleBackoff_NeverExceedsMaxInterval()
    {
        var svc = Build(@base: 30, max: 300, backoff: 2.0, idleThresh: 1, min: 5);
        for (int i = 0; i < 20; i++)
            await svc.RecordActivityAsync("n2", hadWork: false);
        var interval = await svc.GetIntervalAsync("n2");
        interval.TotalSeconds.Should().BeLessThanOrEqualTo(300);
    }

    [Fact]
    public async Task ErrorBackoff_OneError_IntervalWithinJitterBounds()
    {
        // 1 error: raw = 30 * 2^1 = 60, jitter ±20% → [48, 72]
        var svc = Build(@base: 30, errBack: 2.0, jitter: 0.20, min: 5, max: 300);
        await svc.RecordErrorAsync("node-e");
        var interval = await svc.GetIntervalAsync("node-e");
        interval.TotalSeconds.Should().BeInRange(48.0, 72.0);
    }

    [Fact]
    public async Task ErrorBackoff_TwoErrors_IntervalWithinJitterBounds()
    {
        // 2 errors: raw = 30 * 2^2 = 120, jitter ±20% → [96, 144]
        var svc = Build(@base: 30, errBack: 2.0, jitter: 0.20, min: 5, max: 300);
        await svc.RecordErrorAsync("node-e2");
        await svc.RecordErrorAsync("node-e2");
        var interval = await svc.GetIntervalAsync("node-e2");
        interval.TotalSeconds.Should().BeInRange(96.0, 144.0);
    }

    [Fact]
    public async Task ErrorBackoff_ExceedMaxCount_CappedAtMaxInterval()
    {
        // MaxErrorBackoffCount=5; 6 errors → capped at MaxIntervalSeconds ± jitter
        var svc = Build(@base: 30, errBack: 2.0, jitter: 0.20, maxErr: 5, max: 300, min: 5);
        for (int i = 0; i < 6; i++)
            await svc.RecordErrorAsync("node-cap");
        var interval = await svc.GetIntervalAsync("node-cap");
        // max=300, jitter ±20% → upper bound 360 but clamped to max → 300
        interval.TotalSeconds.Should().BeInRange(240.0, 300.0);
    }

    [Fact]
    public async Task Reset_AfterBusyCycles_ReturnsBaseInterval()
    {
        var svc = Build(@base: 30, min: 5, busyThresh: 3);
        await svc.RecordActivityAsync("node-r", hadWork: true);
        await svc.RecordActivityAsync("node-r", hadWork: true);
        await svc.RecordActivityAsync("node-r", hadWork: true);
        await svc.ResetAsync("node-r");
        (await svc.GetIntervalAsync("node-r")).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ErrorClearsOnActivity_AfterThreeErrors_BusyPathTakesOver()
    {
        var svc = Build(@base: 30, min: 5, errBack: 2.0, jitter: 0.0, busyThresh: 3, max: 300);
        await svc.RecordErrorAsync("node-clr");
        await svc.RecordErrorAsync("node-clr");
        await svc.RecordErrorAsync("node-clr");
        // now record 3 busy cycles (which also clears error state)
        await svc.RecordActivityAsync("node-clr", hadWork: true);
        await svc.RecordActivityAsync("node-clr", hadWork: true);
        await svc.RecordActivityAsync("node-clr", hadWork: true);
        // Should be in busy path, not error path
        var interval = await svc.GetIntervalAsync("node-clr");
        interval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IdleCycle_AfterBusy_ResetsBusyCounter()
    {
        var svc = Build(@base: 30, min: 5, busyThresh: 3, idleThresh: 2);
        await svc.RecordActivityAsync("node-m", hadWork: true);
        await svc.RecordActivityAsync("node-m", hadWork: true);
        // One idle resets busy counter
        await svc.RecordActivityAsync("node-m", hadWork: false);
        // Still only 1 idle cycle < idleThresh=2 → base interval
        (await svc.GetIntervalAsync("node-m")).Should().Be(TimeSpan.FromSeconds(30));
    }

    public void Dispose() => _cache.Dispose();
}
```

- [ ] **Step 6: Run tests to verify they fail**

```bash
dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj --filter "AdaptivePollingServiceTests" -v m
```

Expected: compile error — `AdaptivePollingService` does not exist yet.

- [ ] **Step 7: Create `AdaptivePollingService`**

Create `src/MSOSync.Scheduler/AdaptivePollingService.cs`:

```csharp
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
```

- [ ] **Step 8: Run tests to verify they pass**

```bash
dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj --filter "AdaptivePollingServiceTests" -v m
```

Expected: all 10 tests PASS.

- [ ] **Step 9: Run full Scheduler test suite to verify no regressions**

```bash
dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj -v m
```

Expected: all existing tests (`SyncJobTests`, `RetryJobTests`, `PurgeJobTests`, `PullJobTests`, `ReplayWorkerTests`, `RollingOperationWorkerTests`) and the new `AdaptivePollingServiceTests` PASS.

- [ ] **Step 10: Commit adaptive polling service**

```bash
git add src/MSOSync.Scheduler/MSOSync.Scheduler.csproj \
        src/MSOSync.Scheduler/NodePollingState.cs \
        src/MSOSync.Scheduler/Options/AdaptivePollingOptions.cs \
        src/MSOSync.Scheduler/IAdaptivePollingService.cs \
        src/MSOSync.Scheduler/AdaptivePollingService.cs \
        tests/MSOSync.SchedulerTests/AdaptivePollingServiceTests.cs
git commit -m "feat(2D.5-T2): add IAdaptivePollingService + AdaptivePollingService with backoff algorithm"
```
