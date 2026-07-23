using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace MSOSync.IntegrationTests.Caching;

[Trait("Category", "Integration")]
public sealed class RedisCacheIntegrationTests : IAsyncLifetime
{
    private RedisContainer?         _container;
    private RedisCacheService?      _svc;
    private IConnectionMultiplexer? _mux;

    public async Task InitializeAsync()
    {
        if (!DockerIsAvailable())
        {
            // Tests will be skipped via SkipIfUnavailable()
            return;
        }

        _container = new RedisBuilder().Build();
        await _container.StartAsync();

        _mux = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var opts   = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        var logger = NullLogger<RedisCacheService>.Instance;
        _svc       = new RedisCacheService(_mux, opts, logger);
    }

    public async Task DisposeAsync()
    {
        _mux?.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private void SkipIfUnavailable()
    {
        if (_svc is null)
            throw new SkipException("Docker unavailable — Redis integration tests skipped.");
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_SetThenGet_ReturnsCorrectValue()
    {
        SkipIfUnavailable();
        var dto = new CacheTestDto("Integration", 99);

        await _svc!.SetAsync("it:round-trip", dto);
        var result = await _svc.GetAsync<CacheTestDto>("it:round-trip");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Integration");
        result.Value.Should().Be(99);
    }

    // ── Expiry ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Expiry_ValueAbsentAfterTtl()
    {
        SkipIfUnavailable();
        await _svc!.SetAsync("it:expiry", "expires-soon", TimeSpan.FromMilliseconds(100));

        await Task.Delay(300);

        var result = await _svc.GetAsync<string>("it:expiry");
        result.Should().BeNull();
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_AfterSet_ReturnsNull()
    {
        SkipIfUnavailable();
        await _svc!.SetAsync("it:remove", "to-delete");
        await _svc.RemoveAsync("it:remove");

        var result = await _svc.GetAsync<string>("it:remove");
        result.Should().BeNull();
    }

    // ── RemoveByPrefix ────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveByPrefix_DeletesAllKeysWithPrefix()
    {
        SkipIfUnavailable();
        await _svc!.SetAsync("it:prefix:a", "1");
        await _svc.SetAsync("it:prefix:b", "2");
        await _svc.SetAsync("it:prefix:c", "3");
        await _svc.SetAsync("it:other:z", "keep-me");

        await _svc.RemoveByPrefixAsync("it:prefix:");

        (await _svc.GetAsync<string>("it:prefix:a")).Should().BeNull();
        (await _svc.GetAsync<string>("it:prefix:b")).Should().BeNull();
        (await _svc.GetAsync<string>("it:prefix:c")).Should().BeNull();
        (await _svc.GetAsync<string>("it:other:z")).Should().Be("keep-me");
    }

    // ── Large payload ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LargePayload_10KbJson_RoundTripsCorrectly()
    {
        SkipIfUnavailable();
        var items = Enumerable.Range(0, 500)
            .Select(i => new CacheTestDto($"Item-{i}", i))
            .ToList();

        await _svc!.SetAsync("it:large", items);
        var result = await _svc.GetAsync<List<CacheTestDto>>("it:large");

        result.Should().HaveCount(500);
        result![0].Name.Should().Be("Item-0");
        result[499].Value.Should().Be(499);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool DockerIsAvailable()
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName               = "docker",
                Arguments              = "info",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false
            });
            proc!.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private sealed record CacheTestDto(string Name, int Value);
}

/// <summary>
/// xUnit v2 does not have built-in skip. This exception causes the test runner
/// to report the test as failed with a "skip" message. For full skip support,
/// use the xunit.skippable.fact package or filter on Trait("Category","Integration").
/// </summary>
internal sealed class SkipException(string reason) : Exception(reason);
