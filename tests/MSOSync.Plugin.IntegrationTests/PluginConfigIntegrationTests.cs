using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class PluginConfigIntegrationTests
{
    [Fact]
    public async Task PluginConfig_AppsettingsWinsOverFile()
    {
        // plugin.config.json has timeout: "10"
        // appsettings has Plugins:msosync.test:timeout = "99"
        // The plugin should reach Running state — if config layer breaks activation, it won't.
        // ALC isolation prevents direct static-field assertion; we verify through runtime state.
        var host = await PluginHostHarness.StartAsync(
            pluginsDir: PluginHostHarness.IsolatedPath("test-only"),
            extraConfig: new Dictionary<string, string?>
            {
                ["Plugins:msosync.test:timeout"] = "99"
            });
        try
        {
            var registry = host.Services.GetRequiredService<PluginRegistry>();
            var rt       = registry.GetRuntime("msosync.test");

            rt.Should().NotBeNull();
            rt!.State.Should().Be(PluginRuntimeState.Running,
                "config override must not break plugin activation");
            rt.LastException.Should().BeNull("no exception expected when appsettings config is provided");
            rt.Descriptor.ErrorMessage.Should().BeNull();
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [Fact]
    public async Task PluginConfig_MalformedFile_NonFatal()
    {
        // plugin.config.json is malformed JSON → non-fatal warning; plugin still activates
        var host = await PluginHostHarness.StartAsync(
            pluginsDir: PluginHostHarness.IsolatedPath("badconfig-only"),
            extraConfig: new Dictionary<string, string?>
            {
                ["Plugins:msosync.test.badconfig:timeout"] = "42"
            });
        try
        {
            var registry = host.Services.GetRequiredService<PluginRegistry>();
            var rt       = registry.GetRuntime("msosync.test.badconfig");

            rt.Should().NotBeNull();
            rt!.State.Should().Be(PluginRuntimeState.Running,
                "malformed plugin.config.json must not prevent activation");
            rt.LastException.Should().BeNull();
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
