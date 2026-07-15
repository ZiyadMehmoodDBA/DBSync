using FluentAssertions;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;
using MSOSync.Sdk.Metadata;
using Moq;
using Xunit;

namespace MSOSync.SdkTests;

public sealed class PluginBaseTests
{
    private sealed class ConcretePlugin : PluginBase { }

    private static IPluginContext FakeContext()
    {
        var ctx = new Mock<IPluginContext>();
        ctx.Setup(c => c.Metadata).Returns(new PluginMetadata
        {
            PluginId    = "test",
            Name        = "Test",
            Version     = "1.0.0",
            SdkVersion  = "1.0",
            ApiVersion  = "1",
            Author      = "Test",
            Description = "desc"
        });
        return ctx.Object;
    }

    [Fact]
    public async Task InitializeAsync_DefaultImpl_SetsContext()
    {
        var plugin  = new ConcretePlugin();
        var context = FakeContext();

        await plugin.InitializeAsync(context, default);

        // Access via reflection since Context is protected
        var prop = typeof(PluginBase).GetProperty("Context",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var value = prop!.GetValue(plugin);
        value.Should().NotBeNull();
    }

    [Fact]
    public async Task StartAsync_DefaultImpl_ReturnsCompleted()
    {
        var plugin = new ConcretePlugin();
        var act    = () => plugin.StartAsync(default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_DefaultImpl_ReturnsCompleted()
    {
        var plugin = new ConcretePlugin();
        var act    = () => plugin.StopAsync(default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_DefaultImpl_ReturnsCompleted()
    {
        var plugin = new ConcretePlugin();
        var act    = () => plugin.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_CachesContext_ContextPropertyAvailable()
    {
        var plugin  = new ConcretePlugin();
        var context = FakeContext();

        await plugin.InitializeAsync(context, default);

        // Access via reflection since Context is protected
        var prop = typeof(PluginBase).GetProperty("Context",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var value = prop!.GetValue(plugin);
        value.Should().BeSameAs(context);
    }
}
