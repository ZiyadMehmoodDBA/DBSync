using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Diagnostics;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Registry;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class HealthAndCompatTests
{
    [Fact]
    public async Task SdkVersion_Mismatch_PluginFailed()
    {
        // plugin.json declares sdkVersion: "2.0" — host supports major version "1" only
        var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.IsolatedPath("badsdk-only"));
        try
        {
            var registry = host.Services.GetRequiredService<PluginRegistry>();
            var rt       = registry.GetRuntime("msosync.badsdk");

            rt.Should().NotBeNull();
            rt!.State.Should().Be(PluginRuntimeState.Failed,
                "SDK major version mismatch must result in Failed state");
            rt.LastException.Should().NotBeNull();
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task Health_FailedPlugin_ReturnsDegraded()
    {
        // Use the badsdk-only isolated dir — it fails with SdkCompatibility → registry has Failed plugin
        // PluginHealthCheck must report Degraded when any plugin is in Failed status
        var host = await PluginHostHarness.StartAsync(
            PluginHostHarness.IsolatedPath("badsdk-only"));
        try
        {
            // Instantiate PluginHealthCheck directly using the registry from DI
            var registry    = host.Services.GetRequiredService<MSOSync.Plugin.Abstractions.IPluginRegistry>();
            var healthCheck = new PluginHealthCheck(registry);

            var ctx    = new HealthCheckContext { Registration = new HealthCheckRegistration("plugins", healthCheck, null, null) };
            var result = await healthCheck.CheckHealthAsync(ctx, CancellationToken.None);

            result.Status.Should().Be(HealthStatus.Degraded,
                "a Failed plugin must degrade the health check");
            result.Description.Should().Contain("msosync.badsdk");
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
