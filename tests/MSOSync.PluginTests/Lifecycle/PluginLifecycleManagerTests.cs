using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Sdk.Abstractions;
using Xunit;

namespace MSOSync.PluginTests.Lifecycle;

public sealed class PluginLifecycleManagerTests
{
    private readonly PluginRegistry _registry = new();

    private PluginLifecycleManager MakeManager(int timeoutSeconds = 30)
        => new(_registry,
            Options.Create(new PluginHostOptions { DefaultTimeoutSeconds = timeoutSeconds }),
            NullLogger<PluginLifecycleManager>.Instance);

    private (PluginRuntime, Mock<IPlugin>) RegisterPlugin(
        string pluginId, int startupOrder = 1000)
    {
        var pluginMock = new Mock<IPlugin>();
        pluginMock.Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        pluginMock.Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        pluginMock.Setup(p => p.StopAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        pluginMock.Setup(p => p.DisposeAsync())
                  .Returns(ValueTask.CompletedTask);

        var descriptor = new PluginDescriptor
        {
            PluginId     = pluginId, Name = pluginId, Version = "1.0.0",
            Status       = PluginStatus.Loaded,  LoadedAt = DateTime.UtcNow,
            StartupOrder = startupOrder,
        };
        _registry.Register(descriptor);
        var runtime       = _registry.GetRuntime(pluginId)!;
        runtime.Instance  = pluginMock.Object;
        runtime.Context   = Mock.Of<IPluginContext>();
        return (runtime, pluginMock);
    }

    // ── InitializeAllAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAllAsync_HappyPath_StateBecomesInitialized()
    {
        var (rt, _) = RegisterPlugin("p");
        await MakeManager().InitializeAllAsync(default);
        rt.State.Should().Be(PluginRuntimeState.Initialized);
    }

    [Fact]
    public async Task InitializeAllAsync_PluginThrows_StateFailed_OtherPluginContinues()
    {
        var (rtFail, failMock) = RegisterPlugin("fail");
        var (rtOk, _)          = RegisterPlugin("ok");

        failMock.Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("boom"));

        await MakeManager().InitializeAllAsync(default);

        rtFail.State.Should().Be(PluginRuntimeState.Failed);
        rtOk.State.Should().Be(PluginRuntimeState.Initialized);
    }

    [Fact]
    public async Task InitializeAllAsync_Timeout_StateFailed()
    {
        var (rt, pluginMock) = RegisterPlugin("slow");

        pluginMock.Setup(p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()))
                  .Returns<IPluginContext, CancellationToken>(async (_, ct) =>
                      await Task.Delay(TimeSpan.FromSeconds(10), ct));

        var mgr = new PluginLifecycleManager(_registry,
            Options.Create(new PluginHostOptions { DefaultTimeoutSeconds = 1 }),
            NullLogger<PluginLifecycleManager>.Instance);

        await mgr.InitializeAllAsync(default);

        rt.State.Should().Be(PluginRuntimeState.Failed);
        rt.Descriptor.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task InitializeAllAsync_SkipsNonLoaded_Plugins()
    {
        var (rt, pluginMock) = RegisterPlugin("p");
        rt.State = PluginRuntimeState.Failed; // already failed — should skip

        await MakeManager().InitializeAllAsync(default);

        pluginMock.Verify(
            p => p.InitializeAsync(It.IsAny<IPluginContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── StartAllAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task StartAllAsync_HappyPath_StateBecomesRunning()
    {
        var (rt, _) = RegisterPlugin("p");
        rt.State    = PluginRuntimeState.Initialized;
        await MakeManager().StartAllAsync(default);
        rt.State.Should().Be(PluginRuntimeState.Running);
    }

    [Fact]
    public async Task StartAllAsync_SkipsNotInitialized()
    {
        var (rt, pluginMock) = RegisterPlugin("p");
        rt.State = PluginRuntimeState.Failed; // not initialized

        await MakeManager().StartAllAsync(default);

        pluginMock.Verify(p => p.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAllAsync_StartupOrder_AscendingOrder()
    {
        var order = new List<string>();

        void Track(string id) => order.Add(id);

        var (_, m1) = RegisterPlugin("z", startupOrder: 300);
        var (_, m2) = RegisterPlugin("a", startupOrder: 100);
        var (_, m3) = RegisterPlugin("m", startupOrder: 200);

        _registry.GetRuntime("z")!.State = PluginRuntimeState.Initialized;
        _registry.GetRuntime("a")!.State = PluginRuntimeState.Initialized;
        _registry.GetRuntime("m")!.State = PluginRuntimeState.Initialized;

        m1.Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
           .Callback(() => Track("z")).Returns(Task.CompletedTask);
        m2.Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
           .Callback(() => Track("a")).Returns(Task.CompletedTask);
        m3.Setup(p => p.StartAsync(It.IsAny<CancellationToken>()))
           .Callback(() => Track("m")).Returns(Task.CompletedTask);

        await MakeManager().StartAllAsync(default);

        order.Should().Equal("a", "m", "z");
    }

    // ── StopAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAllAsync_HappyPath_StateBecomesStoped()
    {
        var (rt, _) = RegisterPlugin("p");
        rt.State    = PluginRuntimeState.Running;
        await MakeManager().StopAllAsync(default);
        rt.State.Should().Be(PluginRuntimeState.Stopped);
    }

    [Fact]
    public async Task StopAllAsync_Throws_StateStillStopped_OthersContinue()
    {
        var (rtFail, failMock) = RegisterPlugin("fail");
        var (rtOk, _)          = RegisterPlugin("ok");

        rtFail.State = PluginRuntimeState.Running;
        rtOk.State   = PluginRuntimeState.Running;

        failMock.Setup(p => p.StopAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("stop error"));

        await MakeManager().StopAllAsync(default);

        rtFail.State.Should().Be(PluginRuntimeState.Stopped); // exception swallowed
        rtOk.State.Should().Be(PluginRuntimeState.Stopped);
    }

    [Fact]
    public async Task StopAllAsync_ShutdownOrder_Descending()
    {
        var order = new List<string>();

        var (_, m1) = RegisterPlugin("z", startupOrder: 300);
        var (_, m2) = RegisterPlugin("a", startupOrder: 100);

        _registry.GetRuntime("z")!.State = PluginRuntimeState.Running;
        _registry.GetRuntime("a")!.State = PluginRuntimeState.Running;

        m1.Setup(p => p.StopAsync(It.IsAny<CancellationToken>()))
           .Callback(() => order.Add("z")).Returns(Task.CompletedTask);
        m2.Setup(p => p.StopAsync(It.IsAny<CancellationToken>()))
           .Callback(() => order.Add("a")).Returns(Task.CompletedTask);

        await MakeManager().StopAllAsync(default);

        order.Should().Equal("z", "a"); // descending by startupOrder
    }

    [Fact]
    public async Task StopAllAsync_SkipsNonRunning_Plugins()
    {
        var (rt, pluginMock) = RegisterPlugin("p");
        rt.State = PluginRuntimeState.Failed; // not Running or Initialized — should skip

        await MakeManager().StopAllAsync(default);

        pluginMock.Verify(p => p.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── DisposeAllAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAllAsync_AlwaysDisposesRunning_And_Stopped()
    {
        var (rtR, _) = RegisterPlugin("running");
        var (rtS, _) = RegisterPlugin("stopped");
        var (rtD, _) = RegisterPlugin("disposed");

        rtR.State = PluginRuntimeState.Running;
        rtS.State = PluginRuntimeState.Stopped;
        rtD.State = PluginRuntimeState.Disposed;

        await MakeManager().DisposeAllAsync(default);

        rtR.State.Should().Be(PluginRuntimeState.Disposed);
        rtS.State.Should().Be(PluginRuntimeState.Disposed);
        rtD.State.Should().Be(PluginRuntimeState.Disposed); // was already disposed — skipped
    }

    [Fact]
    public async Task DisposeAllAsync_Throws_StateStillDisposed_AlwaysSet()
    {
        var (rt, pluginMock) = RegisterPlugin("p");
        rt.State = PluginRuntimeState.Running;

        pluginMock.Setup(p => p.DisposeAsync())
                  .ThrowsAsync(new Exception("dispose error"));

        await MakeManager().DisposeAllAsync(default);

        rt.State.Should().Be(PluginRuntimeState.Disposed);
    }
}
