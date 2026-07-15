using FluentAssertions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using Xunit;

namespace MSOSync.PluginTests.Registry;

public sealed class PluginRegistryTests
{
    private static PluginDescriptor MakeDescriptor(string id, PluginStatus status = PluginStatus.Loaded) => new()
    {
        PluginId = id, Name = id, Version = "1.0.0",
        Status   = status, LoadedAt = DateTime.UtcNow,
    };

    [Fact]
    public void IsInitialized_BeforeMarkInitialized_ReturnsFalse()
    {
        var reg = new PluginRegistry();
        reg.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void MarkInitialized_SetsIsInitializedTrue()
    {
        var reg = new PluginRegistry();
        reg.MarkInitialized();
        reg.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void GetAll_BeforeMarkInitialized_ReturnsEmpty()
    {
        var reg = new PluginRegistry();
        reg.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Register_ThenGetById_ReturnsDescriptor()
    {
        var reg = new PluginRegistry();
        var d   = MakeDescriptor("plugin.a");
        reg.Register(d);
        reg.GetById("plugin.a").Should().NotBeNull();
        reg.GetById("plugin.a")!.PluginId.Should().Be("plugin.a");
    }

    [Fact]
    public void GetById_CaseInsensitive()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.A"));
        reg.GetById("PLUGIN.A").Should().NotBeNull();
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.a"));
        reg.Register(MakeDescriptor("plugin.b"));
        reg.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void Register_Overwrite_ReplacesExisting()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.a", PluginStatus.Failed));
        reg.Register(MakeDescriptor("plugin.a", PluginStatus.Loaded));
        reg.GetById("plugin.a")!.Status.Should().Be(PluginStatus.Loaded);
    }

    [Fact]
    public void UpdateStatus_ExistingPlugin_UpdatesStatus()
    {
        var reg = new PluginRegistry();
        reg.Register(MakeDescriptor("plugin.a", PluginStatus.Loaded));
        reg.UpdateStatus("plugin.a", PluginStatus.Failed, "something broke");
        var d = reg.GetById("plugin.a")!;
        d.Status.Should().Be(PluginStatus.Failed);
        d.ErrorMessage.Should().Be("something broke");
    }

    [Fact]
    public void UpdateStatus_UnknownPlugin_DoesNotThrow()
    {
        var reg = new PluginRegistry();
        var act = () => reg.UpdateStatus("no.such.plugin", PluginStatus.Failed);
        act.Should().NotThrow();
    }
}
