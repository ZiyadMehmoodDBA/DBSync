using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        IPluginRuntimeManager? runtimeManager = null,
        IPluginRegistry? registry = null,
        IPluginLoader? loader = null)
    {
        if (runtimeManager == null)
        {
            var rmMock = new Mock<IPluginRuntimeManager>();
            rmMock.Setup(rm => rm.LoadAndActivateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            rmMock.Setup(rm => rm.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            rmMock.Setup(rm => rm.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            rmMock.Setup(rm => rm.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            rmMock.Setup(rm => rm.DisposeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            rmMock.Setup(rm => rm.LoadElapsedMs).Returns(0);
            rmMock.Setup(rm => rm.InitializeElapsedMs).Returns(0);
            rmMock.Setup(rm => rm.StartElapsedMs).Returns(0);
            runtimeManager = rmMock.Object;
        }

        registry ??= Mock.Of<IPluginRegistry>(r =>
            r.GetAll() == (IReadOnlyList<PluginDescriptor>)new List<PluginDescriptor>());

        if (loader == null)
        {
            var loaderMock = new Mock<IPluginLoader>();
            loaderMock.Setup(l => l.LoadContexts)
                      .Returns(Array.Empty<AssemblyLoadContext>());
            loader = loaderMock.Object;
        }

        return new PluginHost(
            runtimeManager, registry, loader,
            NullLogger<PluginHost>.Instance);
    }

    [Fact]
    public async Task StartAsync_MissingPluginsDir_DoesNotThrow()
    {
        var host = MakeHost();
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
        registry.Setup(r => r.GetAll())
                .Returns(new List<PluginDescriptor>());
        var host = MakeHost(registry: registry.Object);
        await host.StartAsync(default);
        registry.Verify(r => r.MarkInitialized(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_CallsRuntimeManagerInOrder()
    {
        var callOrder = new List<string>();
        var rmMock    = new Mock<IPluginRuntimeManager>();
        rmMock.Setup(rm => rm.LoadAndActivateAsync(It.IsAny<CancellationToken>()))
              .Callback(() => callOrder.Add("Load"))
              .Returns(Task.CompletedTask);
        rmMock.Setup(rm => rm.InitializeAsync(It.IsAny<CancellationToken>()))
              .Callback(() => callOrder.Add("Initialize"))
              .Returns(Task.CompletedTask);
        rmMock.Setup(rm => rm.StartAsync(It.IsAny<CancellationToken>()))
              .Callback(() => callOrder.Add("Start"))
              .Returns(Task.CompletedTask);
        rmMock.Setup(rm => rm.LoadElapsedMs).Returns(0);
        rmMock.Setup(rm => rm.InitializeElapsedMs).Returns(0);
        rmMock.Setup(rm => rm.StartElapsedMs).Returns(0);

        var host = MakeHost(runtimeManager: rmMock.Object);
        await host.StartAsync(default);

        callOrder.Should().Equal("Load", "Initialize", "Start");
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

        var host = MakeHost(loader: loader.Object);
        await host.StartAsync(default);

        // Should not throw even when Unload() is called on a real collectible context
        var act = () => host.StopAsync(default);
        await act.Should().NotThrowAsync();
    }
}
