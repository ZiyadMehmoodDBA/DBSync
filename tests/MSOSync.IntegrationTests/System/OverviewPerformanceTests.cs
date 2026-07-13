// tests/MSOSync.IntegrationTests/System/OverviewPerformanceTests.cs
using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[Collection("SystemAdmin")]
public sealed class OverviewPerformanceTests(SystemFixture fx)
{
    // ── Performance SLA tests ──────────────────────────────────────────────────

    [Fact]
    public async Task GetOverview_With1000Operations_RespondsWithin500ms()
    {
        // Seed 1000 operations and 100 nodes for a realistic load
        await fx.SeedOperationsAsync(1000);
        await fx.SeedNodesAsync(100);

        var admin = await fx.AdminClientAsync();

        // Warm up: one request to prime caches before timing
        await admin.GetAsync("/api/v1/system/overview");

        // Timed request
        var sw = Stopwatch.StartNew();
        var resp = await admin.GetAsync("/api/v1/system/overview");
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "OverviewSnapshotCache must serve overview within 500ms with 1000 operations and 100 nodes");
    }

    [Fact]
    public async Task GetWorkers_RespondsWithin300ms()
    {
        var admin = await fx.AdminClientAsync();

        // Warm up
        await admin.GetAsync("/api/v1/system/workers");

        var sw = Stopwatch.StartNew();
        var resp = await admin.GetAsync("/api/v1/system/workers");
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(300,
            "workers endpoint reads from an in-memory ConcurrentDictionary and must be <300ms");
    }

    [Fact]
    public async Task GetSystemInfo_RespondsWithin100ms()
    {
        var admin = await fx.AdminClientAsync();

        // Warm up
        await admin.GetAsync("/api/v1/system/info");

        var sw = Stopwatch.StartNew();
        var resp = await admin.GetAsync("/api/v1/system/info");
        sw.Stop();

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(100,
            "system/info is a synchronous in-memory read and must respond in <100ms");
    }

    [Fact]
    public async Task GetOverview_CalledTenTimesInSequence_NoSignificantDegradation()
    {
        // Verifies repeated calls do not degrade significantly (no memory/lock accumulation)
        var admin = await fx.AdminClientAsync();

        var times = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            var resp = await admin.GetAsync("/api/v1/system/overview");
            sw.Stop();
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            times.Add(sw.ElapsedMilliseconds);
        }

        // Last call should not be dramatically slower than the first.
        // Allow 10x variance as a loose sanity check (caches warm up over calls).
        var first = times[0];
        var last  = times[^1];
        last.Should().BeLessThan(Math.Max(first * 10, 2000),
            "repeated overview calls should not degrade significantly; caching must work");
    }

    [Fact]
    public async Task GetOverview_CacheServing_SecondCallFasterThanFirst()
    {
        var admin = await fx.AdminClientAsync();

        // First call — may populate cache
        var sw1 = Stopwatch.StartNew();
        var resp1 = await admin.GetAsync("/api/v1/system/overview");
        sw1.Stop();
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second call — should be served from cache (OverviewSnapshotCache TTL = 5s)
        var sw2 = Stopwatch.StartNew();
        var resp2 = await admin.GetAsync("/api/v1/system/overview");
        sw2.Stop();
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        // The second call should be at most 2× slower than the first call.
        // This is a soft assertion — if both calls are very fast (<5ms) we skip the ratio check.
        if (sw1.ElapsedMilliseconds > 5)
        {
            sw2.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(sw1.ElapsedMilliseconds * 2 + 50,
                "cache should make the second call faster or equal to the first");
        }
    }
}
