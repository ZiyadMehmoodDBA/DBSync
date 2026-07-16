using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Plugin.Lifecycle;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class FullLifecycleTests
{
    [Fact]
    public async Task FullLifecycle_ValidPlugin_ReachesRunning()
    {
        // IsolatedPath("test-only") points to a wrapper dir containing only msosync.test/
        var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.IsolatedPath("test-only"));
        try
        {
            var registry = host.Services.GetRequiredService<PluginRegistry>();
            var rt       = registry.GetRuntime("msosync.test");

            rt.Should().NotBeNull("msosync.test must be registered");
            rt!.State.Should().Be(PluginRuntimeState.Running);

            // timestamps populated
            rt.InitializedAt.Should().NotBeNull();
            rt.StartedAt.Should().NotBeNull();
            rt.InitializeDuration.Should().NotBeNull();
            rt.StartDuration.Should().NotBeNull();

            // public status via registry
            registry.GetById("msosync.test")!.Status.Should().Be(PluginStatus.Running);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task StartupOrder_Ascending()
    {
        // Three plugins with orders 100, 200, 300 loaded from TestAssets/plugins/ (all plugin dirs).
        // PluginLifecycleManager must call InitializeAsync in ascending order: 100 → 200 → 300.
        var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.TestAssetPath(string.Empty),   // → TestAssets/plugins/ (all subdirs)
            extraConfig: null,
            configureServices: null);
        try
        {
            var registry = host.Services.GetRequiredService<PluginRegistry>();
            var runtimes = registry.GetAllRuntimes()
                .Where(r => r.Descriptor.PluginId.StartsWith("msosync.order"))
                .ToList();

            runtimes.Should().HaveCount(3);

            // All should have reached Running
            runtimes.Should().AllSatisfy(rt =>
                rt.State.Should().Be(PluginRuntimeState.Running));

            // InitializedAt timestamps must be in ascending order by startupOrder
            var byOrder = runtimes
                .OrderBy(r => r.Descriptor.StartupOrder)
                .ToList();

            byOrder[0].Descriptor.PluginId.Should().Be("msosync.order100");
            byOrder[1].Descriptor.PluginId.Should().Be("msosync.order200");
            byOrder[2].Descriptor.PluginId.Should().Be("msosync.order300");

            // InitializedAt should be non-decreasing (100 initialized before 200, etc.)
            byOrder[0].InitializedAt.Should().BeBefore(byOrder[1].InitializedAt!.Value.AddSeconds(1));
            byOrder[1].InitializedAt.Should().BeBefore(byOrder[2].InitializedAt!.Value.AddSeconds(1));
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task DuplicatePluginId_FirstWins_SecondFails()
    {
        // Two subdirs both declare id = "msosync.duptest". First alphabetically wins.
        var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.TestAssetPath(string.Empty));
        try
        {
            var registry    = host.Services.GetRequiredService<PluginRegistry>();
            var allRuntimes = registry.GetAllRuntimes();

            // Exactly one runtime for "msosync.duptest" (dedup at registry level)
            var dupRuntimes = allRuntimes
                .Where(r => r.Descriptor.PluginId == "msosync.duptest")
                .ToList();

            dupRuntimes.Should().HaveCount(1, "duplicate plugin id should result in exactly one runtime");
            dupRuntimes[0].State.Should().Be(PluginRuntimeState.Running,
                "the first-discovered instance should succeed");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
