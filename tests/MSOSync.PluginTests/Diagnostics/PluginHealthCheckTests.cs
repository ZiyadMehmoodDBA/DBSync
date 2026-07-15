using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Diagnostics;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Diagnostics;

public sealed class PluginHealthCheckTests
{
    private static PluginDescriptor MakePlugin(string id, PluginStatus status) => new()
    {
        PluginId = id, Name = id, Version = "1.0.0",
        Status   = status, LoadedAt = DateTime.UtcNow,
    };

    private static IPluginRegistry RegistryWith(bool initialized, params PluginDescriptor[] plugins)
    {
        var mock = new Mock<IPluginRegistry>();
        mock.Setup(r => r.IsInitialized).Returns(initialized);
        mock.Setup(r => r.GetAll()).Returns(plugins.ToList());
        return mock.Object;
    }

    private static HealthCheckContext FakeContext() =>
        new() { Registration = new HealthCheckRegistration("plugins", Mock.Of<IHealthCheck>(), null, null) };

    [Fact]
    public async Task CheckHealth_RegistryNotInitialized_ReturnsUnhealthy()
    {
        var check  = new PluginHealthCheck(RegistryWith(false));
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_NoEnabledPlugins_ReturnsHealthy()
    {
        var reg    = RegistryWith(true, MakePlugin("p", PluginStatus.Disabled));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_AllLoaded_ReturnsHealthy()
    {
        var reg   = RegistryWith(true,
            MakePlugin("a", PluginStatus.Loaded),
            MakePlugin("b", PluginStatus.Loaded));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_OneFailedPlugin_ReturnsDegraded()
    {
        var reg   = RegistryWith(true,
            MakePlugin("a", PluginStatus.Loaded),
            MakePlugin("b", PluginStatus.Failed));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("b");
    }

    [Fact]
    public async Task CheckHealth_DisabledExcludedFromDegraded()
    {
        var reg   = RegistryWith(true,
            MakePlugin("loaded", PluginStatus.Loaded),
            MakePlugin("disabled", PluginStatus.Disabled));
        var check  = new PluginHealthCheck(reg);
        var result = await check.CheckHealthAsync(FakeContext());
        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
