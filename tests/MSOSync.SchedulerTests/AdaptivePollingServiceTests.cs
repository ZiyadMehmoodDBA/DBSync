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
