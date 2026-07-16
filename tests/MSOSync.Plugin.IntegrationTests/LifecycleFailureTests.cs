using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Sdk.Abstractions;
using Xunit;

namespace MSOSync.Plugin.IntegrationTests;

public sealed class LifecycleFailureTests
{
    // Build a PluginLifecycleManager with an empty PluginRegistry and given options.
    private static (PluginLifecycleManager lifecycle, PluginRegistry registry) BuildHarness(
        int defaultTimeoutSeconds = 30)
    {
        var opts      = new PluginHostOptions { DefaultTimeoutSeconds = defaultTimeoutSeconds };
        var registry  = new PluginRegistry();
        var logger    = NullLogger<PluginLifecycleManager>.Instance;
        var lifecycle = new PluginLifecycleManager(registry, Options.Create(opts), logger);
        return (lifecycle, registry);
    }

    // Helper: add a pre-built runtime (with mock instance) directly to the registry.
    private static PluginRuntime AddRuntime(
        PluginRegistry registry,
        string pluginId,
        IPlugin plugin,
        int startupOrder = 1000,
        PluginRuntimeState state = PluginRuntimeState.Loaded)
    {
        var descriptor = new PluginDescriptor
        {
            PluginId     = pluginId,
            Name         = pluginId,
            Version      = "1.0.0",
            Status       = PluginStatus.Loaded,
            StartupOrder = startupOrder,
        };
        registry.Register(descriptor);
        var rt    = registry.GetRuntime(pluginId)!;
        rt.Instance = plugin;
        rt.State    = state;
        return rt;
    }

    [Fact]
    public async Task InitializeAsync_Timeout_PluginFailed_OthersContinue()
    {
        var (lifecycle, registry) = BuildHarness(defaultTimeoutSeconds: 1);

        // Slow plugin: hangs until cancelled
        var slowMock = new Mock<IPlugin>();
        slowMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns<IPluginContext, CancellationToken>(async (_, ct) =>
                await Task.Delay(TimeSpan.FromMinutes(10), ct));

        // Fast plugin: succeeds immediately
        var fastMock = new Mock<IPlugin>();
        fastMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var slowRt = AddRuntime(registry, "slow.plugin", slowMock.Object, startupOrder: 100);
        var fastRt = AddRuntime(registry, "fast.plugin", fastMock.Object, startupOrder: 200);

        await lifecycle.InitializeAllAsync(CancellationToken.None);

        slowRt.State.Should().Be(PluginRuntimeState.Failed, "slow plugin timed out");
        slowRt.LastException.Should().NotBeNull();

        fastRt.State.Should().Be(PluginRuntimeState.Initialized, "fast plugin should succeed despite slow one failing");
    }

    [Fact]
    public async Task StartAsync_Throws_PluginFailed_OthersContinue()
    {
        var (lifecycle, registry) = BuildHarness();

        // Throwing plugin: throws in StartAsync
        var throwingMock = new Mock<IPlugin>();
        throwingMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        throwingMock
            .Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Good plugin: all methods succeed
        var goodMock = new Mock<IPlugin>();
        goodMock
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        goodMock
            .Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var throwingRt = AddRuntime(registry, "throwing.plugin", throwingMock.Object, startupOrder: 100);
        var goodRt     = AddRuntime(registry, "good.plugin",     goodMock.Object,     startupOrder: 200);

        // Initialize both first
        await lifecycle.InitializeAllAsync(CancellationToken.None);
        throwingRt.State.Should().Be(PluginRuntimeState.Initialized);
        goodRt.State.Should().Be(PluginRuntimeState.Initialized);

        // Now start — throwing plugin fails, good plugin continues
        await lifecycle.StartAllAsync(CancellationToken.None);

        throwingRt.State.Should().Be(PluginRuntimeState.Failed);
        throwingRt.LastException!.Message.Should().Be("boom");
        goodRt.State.Should().Be(PluginRuntimeState.Running);
    }

    [Fact]
    public async Task StopAsync_Throws_Logged_OthersStopped()
    {
        var (lifecycle, registry) = BuildHarness();

        // Plugin that throws in StopAsync
        var throwingStop = new Mock<IPlugin>();
        throwingStop
            .Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        throwingStop
            .Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        throwingStop
            .Setup(p => p.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("stop failed"));
        throwingStop
            .Setup(p => p.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        // Normal plugin
        var normalMock = new Mock<IPlugin>();
        normalMock.Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        normalMock.Setup(p => p.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        normalMock.Setup(p => p.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        normalMock.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var throwingRt = AddRuntime(registry, "throw.stop",  throwingStop.Object, startupOrder: 200);
        var normalRt   = AddRuntime(registry, "normal.stop", normalMock.Object,   startupOrder: 100);

        // Get both to Running
        await lifecycle.InitializeAllAsync(CancellationToken.None);
        await lifecycle.StartAllAsync(CancellationToken.None);

        throwingRt.State.Should().Be(PluginRuntimeState.Running);
        normalRt.State.Should().Be(PluginRuntimeState.Running);

        // Stop — the throwing plugin's exception must be swallowed; both stop
        // StopAllAsync must NOT throw even if one plugin does
        var act = async () => await lifecycle.StopAllAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // State is Stopped regardless of exception during StopAsync
        throwingRt.State.Should().Be(PluginRuntimeState.Stopped,
            "exceptions during StopAsync are swallowed; state still becomes Stopped");
        normalRt.State.Should().Be(PluginRuntimeState.Stopped);
    }
}
