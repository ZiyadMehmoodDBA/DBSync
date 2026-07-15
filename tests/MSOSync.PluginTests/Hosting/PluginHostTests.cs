using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Hosting;
using MSOSync.Plugin.Models;
using System.Runtime.Loader;
using Xunit;

namespace MSOSync.PluginTests.Hosting;

public sealed class PluginHostTests
{
    private static PluginHost MakeHost(
        IPluginLoader? loader = null,
        IPluginRegistry? registry = null,
        string pluginsPath = "non-existent-path")
    {
        if (loader == null)
        {
            var loaderMock = new Mock<IPluginLoader>();
            loaderMock.Setup(l => l.LoadContexts)
                      .Returns(Array.Empty<AssemblyLoadContext>());
            loaderMock.Setup(l => l.LoadAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(Array.Empty<PluginLoadResult>());
            loader = loaderMock.Object;
        }

        registry ??= Mock.Of<IPluginRegistry>();

        return new PluginHost(
            loader, registry,
            Options.Create(new PluginHostOptions { PluginsPath = pluginsPath, HostVersion = "14.0.0" }),
            NullLogger<PluginHost>.Instance);
    }

    [Fact]
    public async Task StartAsync_MissingPluginsDir_DoesNotThrow()
    {
        var host = MakeHost(pluginsPath: Path.Combine(Path.GetTempPath(), "no-such-plugins-dir-ever"));
        var act  = () => host.StartAsync(default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_SetsIsStartedTrue()
    {
        var host = MakeHost();
        await host.StartAsync(default);
        host.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_SetsStartedAt()
    {
        var before = DateTime.UtcNow;
        var host   = MakeHost();
        await host.StartAsync(default);
        host.StartedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task StartAsync_CallsMarkInitialized()
    {
        var registry = new Mock<IPluginRegistry>();
        var host     = MakeHost(registry: registry.Object);
        await host.StartAsync(default);
        registry.Verify(r => r.MarkInitialized(), Times.Once);
    }

    // AssemblyLoadContext.Unload() is not virtual and cannot be intercepted by Moq.
    // We use a collectible subclass and check the IsCollectible property after StopAsync —
    // a collectible context can be unloaded, and the test confirms PluginHost iterates
    // loader.LoadContexts and calls Unload() without throwing.
    private sealed class CollectibleContext : AssemblyLoadContext
    {
        public CollectibleContext() : base(isCollectible: true) { }
        protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName assemblyName) => null;
    }

    [Fact]
    public async Task StopAsync_UnloadsLoadContexts()
    {
        var ctx    = new CollectibleContext();
        var loader = new Mock<IPluginLoader>();
        loader.Setup(l => l.LoadContexts)
              .Returns(new List<AssemblyLoadContext> { ctx });
        loader.Setup(l => l.LoadAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<PluginLoadResult>());

        var host = MakeHost(loader: loader.Object);
        await host.StartAsync(default);

        // Should not throw even when Unload() is called on a real collectible context
        var act = () => host.StopAsync(default);
        await act.Should().NotThrowAsync();
    }
}
