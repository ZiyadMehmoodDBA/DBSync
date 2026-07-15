# Epic 14B — Task 7: PluginLifecycleManager

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Create `PluginRuntimeState` enum and `PluginLifecycleManager` which drives `IPlugin` instances through `InitializeAsync → StartAsync → StopAsync → DisposeAsync` with timeout enforcement, failure isolation, and startup/shutdown ordering. Write table-driven unit tests covering all critical transitions.

**Architecture:** `PluginRuntimeState` is internal (11 states). `PluginLifecycleManager` iterates all runtimes from `PluginRegistry`, sorts by `startupOrder`/`PluginId`, and runs each phase. Each phase uses a `CancellationTokenSource.CreateLinkedTokenSource(hostCt)` with `CancelAfter(timeout)`. Host shutdown cancellation vs timeout are distinguished post-catch by inspecting `hostCt.IsCancellationRequested`. Failure isolation: one plugin's exception never stops the others. `DisposeAsync` runs on all plugins regardless of state (except `Disposed` and `Disabled`).

**Tech Stack:** C# 13 / .NET 9 / xUnit + FluentAssertions + Moq

## Global Constraints

- `PluginRuntimeState` is `internal enum` — 11 values: `Loaded, Initializing, Initialized, Starting, Running, Stopping, Stopped, Disposing, Disposed, Failed, Disabled`
- Startup order: ascending `StartupOrder`, then ascending `PluginId` for ties (use `StringComparer.OrdinalIgnoreCase`)
- Shutdown order: descending (reverse of startup)
- Timeout fires → `OperationCanceledException` caught → check `hostCt.IsCancellationRequested`: if false → timeout → set `Failed`; if true → normal shutdown, do NOT override state to Failed
- `StopAsync` exceptions: logged, never rethrown; continue with remaining plugins; state → `Stopped` unless timeout
- `DisposeAsync` exceptions: logged, never rethrown; state always → `Disposed`
- Skip `InitializeAsync` for plugins not in `Loaded` state; skip `StartAsync` for not-`Initialized`; skip `StopAsync` for not `Running` or `Initialized`

## Files

**Create:**
- `src/MSOSync.Plugin/Lifecycle/PluginRuntimeState.cs`
- `src/MSOSync.Plugin/Lifecycle/PluginLifecycleManager.cs`
- `tests/MSOSync.PluginTests/Lifecycle/PluginLifecycleManagerTests.cs`

## Interfaces

**Consumes:**
- `PluginRegistry.GetAllRuntimes()` — internal method (added in Task 6)
- `PluginRuntime` with `Instance`, `State`, `LastException`, `InitializedAt`, `StartedAt`, `StoppedAt`, `DisposedAt`, `InitializeDuration`, `StartDuration`, `StopDuration`, `DisposeDuration` (added in Task 8 — the tests mock it)
- `IPlugin` from Task 1
- `PluginHostOptions.DefaultTimeoutSeconds`, `InitializeTimeoutSeconds`, `StartTimeoutSeconds`, `StopTimeoutSeconds`, `DisposeTimeoutSeconds` (added in Task 8)

**Produces:**
- `PluginRuntimeState` enum (used by PluginRuntime in Task 8)
- `PluginLifecycleManager(PluginRegistry, IOptions<PluginHostOptions>, ILogger<PluginLifecycleManager>)`
  - `Task InitializeAllAsync(CancellationToken hostCt)`
  - `Task StartAllAsync(CancellationToken hostCt)`
  - `Task StopAllAsync(CancellationToken hostCt)`
  - `Task DisposeAllAsync(CancellationToken hostCt)`

---

- [ ] **Step 1: Create `src/MSOSync.Plugin/Lifecycle/PluginRuntimeState.cs`**

```csharp
namespace MSOSync.Plugin.Lifecycle;

internal enum PluginRuntimeState
{
    Loaded,       // 14A end state: assembly loaded, type verified
    Initializing, // InitializeAsync in progress
    Initialized,  // InitializeAsync completed
    Starting,     // StartAsync in progress
    Running,      // StartAsync completed — steady state
    Stopping,     // StopAsync in progress
    Stopped,      // StopAsync completed (always host-initiated)
    Disposing,    // DisposeAsync in progress
    Disposed,     // DisposeAsync completed
    Failed,       // any phase failed — LastException set
    Disabled      // filtered at stage 4 — never receives lifecycle calls
}
```

- [ ] **Step 2: Create `src/MSOSync.Plugin/Lifecycle/PluginLifecycleManager.cs`**

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;

namespace MSOSync.Plugin.Lifecycle;

internal sealed class PluginLifecycleManager(
    PluginRegistry               registry,
    IOptions<PluginHostOptions>  options,
    ILogger<PluginLifecycleManager> logger)
{
    public async Task InitializeAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForStartup();
        foreach (var rt in runtimes)
        {
            if (rt.State != PluginRuntimeState.Loaded) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Initializing;
            var sw   = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
                cts.CancelAfter(TimeSpan.FromSeconds(InitTimeout));

                using (BeginPhaseScope(rt.Descriptor.PluginId, rt.Descriptor.Version, "Initialize"))
                {
                    await rt.Instance.InitializeAsync(rt.Context!, cts.Token);
                }

                sw.Stop();
                rt.InitializeDuration = sw.Elapsed;
                rt.InitializedAt      = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.InitializedAt.Value;
                rt.State              = PluginRuntimeState.Initialized;

                logger.Log(LogLevel.Information, PluginLogEvents.PluginInitialized,
                    "Plugin {PluginId} initialized in {Ms}ms", rt.Descriptor.PluginId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
            {
                sw.Stop();
                var msg = $"InitializeAsync timed out after {InitTimeout}s";
                SetFailed(rt, msg, sw.Elapsed);
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginTimeout,
                    "Plugin {PluginId} InitializeAsync timed out", rt.Descriptor.PluginId);
            }
            catch (Exception ex)
            {
                sw.Stop();
                SetFailed(rt, ex.Message, sw.Elapsed);
                logger.Log(LogLevel.Error, PluginLogEvents.PluginFailed,
                    ex, "Plugin {PluginId} InitializeAsync failed", rt.Descriptor.PluginId);
            }
        }
    }

    public async Task StartAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForStartup();
        foreach (var rt in runtimes)
        {
            if (rt.State != PluginRuntimeState.Initialized) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Starting;
            var sw   = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
                cts.CancelAfter(TimeSpan.FromSeconds(StartTimeout));

                using (BeginPhaseScope(rt.Descriptor.PluginId, rt.Descriptor.Version, "Start"))
                {
                    await rt.Instance.StartAsync(cts.Token);
                }

                sw.Stop();
                rt.StartDuration      = sw.Elapsed;
                rt.StartedAt          = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.StartedAt.Value;
                rt.State              = PluginRuntimeState.Running;

                // TotalDuration = LoadDurationMs (from descriptor) + initialize + start
                var loadMs = TimeSpan.FromMilliseconds(rt.Descriptor.LoadDurationMs);
                rt.TotalDuration = loadMs + (rt.InitializeDuration ?? TimeSpan.Zero) + sw.Elapsed;

                logger.Log(LogLevel.Information, PluginLogEvents.PluginStarted,
                    "Plugin {PluginId} started in {Ms}ms", rt.Descriptor.PluginId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
            {
                sw.Stop();
                SetFailed(rt, $"StartAsync timed out after {StartTimeout}s", sw.Elapsed);
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginTimeout,
                    "Plugin {PluginId} StartAsync timed out", rt.Descriptor.PluginId);
            }
            catch (Exception ex)
            {
                sw.Stop();
                SetFailed(rt, ex.Message, sw.Elapsed);
                logger.Log(LogLevel.Error, PluginLogEvents.PluginFailed,
                    ex, "Plugin {PluginId} StartAsync failed", rt.Descriptor.PluginId);
            }
        }
    }

    public async Task StopAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForShutdown();
        foreach (var rt in runtimes)
        {
            if (rt.State is not (PluginRuntimeState.Running or PluginRuntimeState.Initialized)) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Stopping;
            var sw   = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
                cts.CancelAfter(TimeSpan.FromSeconds(StopTimeout));

                using (BeginPhaseScope(rt.Descriptor.PluginId, rt.Descriptor.Version, "Stop"))
                {
                    await rt.Instance.StopAsync(cts.Token);
                }

                sw.Stop();
                rt.StopDuration       = sw.Elapsed;
                rt.StoppedAt          = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.StoppedAt.Value;
                rt.State              = PluginRuntimeState.Stopped;

                logger.Log(LogLevel.Information, PluginLogEvents.PluginStopped,
                    "Plugin {PluginId} stopped in {Ms}ms", rt.Descriptor.PluginId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
            {
                sw.Stop();
                SetFailed(rt, $"StopAsync timed out after {StopTimeout}s", sw.Elapsed);
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginTimeout,
                    "Plugin {PluginId} StopAsync timed out", rt.Descriptor.PluginId);
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Stop exceptions are non-fatal — log and continue
                rt.StopDuration       = sw.Elapsed;
                rt.StoppedAt          = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.StoppedAt.Value;
                rt.State              = PluginRuntimeState.Stopped;
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginFailed,
                    ex, "Plugin {PluginId} StopAsync threw; treating as stopped", rt.Descriptor.PluginId);
            }
        }
    }

    public async Task DisposeAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForShutdown();
        foreach (var rt in runtimes)
        {
            if (rt.State is PluginRuntimeState.Disposed or PluginRuntimeState.Disabled) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Disposing;
            var sw   = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
                cts.CancelAfter(TimeSpan.FromSeconds(DisposeTimeout));

                await rt.Instance.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginDisposed,
                    ex, "Plugin {PluginId} DisposeAsync threw; ignoring", rt.Descriptor.PluginId);
            }
            finally
            {
                sw.Stop();
                rt.DisposeDuration    = sw.Elapsed;
                rt.DisposedAt         = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.DisposedAt.Value;
                rt.State              = PluginRuntimeState.Disposed;

                logger.Log(LogLevel.Debug, PluginLogEvents.PluginDisposed,
                    "Plugin {PluginId} disposed", rt.Descriptor.PluginId);
            }
        }
    }

    private List<PluginRuntime> SortedForStartup()
        => registry.GetAllRuntimes()
            .OrderBy(r => r.Descriptor.StartupOrder)
            .ThenBy(r => r.Descriptor.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<PluginRuntime> SortedForShutdown()
        => registry.GetAllRuntimes()
            .OrderByDescending(r => r.Descriptor.StartupOrder)
            .ThenByDescending(r => r.Descriptor.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void SetFailed(PluginRuntime rt, string message, TimeSpan elapsed)
    {
        rt.LastException          = new InvalidOperationException(message);
        rt.State                  = PluginRuntimeState.Failed;
        rt.LastStateChangeUtc     = DateTime.UtcNow;
        rt.Descriptor.Status      = PluginStatus.Failed;
        rt.Descriptor.ErrorMessage = message;
    }

    private static IDisposable? BeginPhaseScope(string pluginId, string version, string phase)
        => null; // real scope wired in Task 8 via ILogger factory

    private int InitTimeout    => options.Value.InitializeTimeoutSeconds ?? options.Value.DefaultTimeoutSeconds;
    private int StartTimeout   => options.Value.StartTimeoutSeconds      ?? options.Value.DefaultTimeoutSeconds;
    private int StopTimeout    => options.Value.StopTimeoutSeconds       ?? options.Value.DefaultTimeoutSeconds;
    private int DisposeTimeout => options.Value.DisposeTimeoutSeconds    ?? options.Value.DefaultTimeoutSeconds;
}
```

Note: `PluginRuntime` needs `State`, `Instance`, `Context`, `LastException`, timestamps, and duration properties — these are added in Task 8. For the lifecycle manager to compile now, we need those properties to exist on `PluginRuntime`. **Add them now as part of this task** (they won't be used fully until Task 8 wires everything):

In `src/MSOSync.Plugin/Models/PluginRuntime.cs`, add the missing properties:

```csharp
using System.Reflection;
using System.Runtime.Loader;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Models;

// Changed from 'sealed record' to 'sealed class' — record equality semantics are wrong
// for a mutable runtime object. Properties use 'set' throughout.
internal sealed class PluginRuntime
{
    public PluginDescriptor      Descriptor     { get; set; } = null!;
    public Assembly?             Assembly       { get; set; }
    public AssemblyLoadContext?  LoadContext    { get; set; }

    // 14B runtime fields
    public IPlugin?              Instance       { get; set; }
    public IServiceProvider?     PluginServices { get; set; }
    public IPluginContext?       Context        { get; set; }
    public PluginRuntimeState    State          { get; set; } = PluginRuntimeState.Loaded;
    public Exception?            LastException  { get; set; }

    // Lifecycle timestamps
    public DateTime? InitializedAt      { get; set; }
    public DateTime? StartedAt          { get; set; }
    public DateTime? StoppedAt          { get; set; }
    public DateTime? DisposedAt         { get; set; }
    public DateTime  LastStateChangeUtc { get; set; }

    // Lifecycle durations
    public TimeSpan? InitializeDuration { get; set; }
    public TimeSpan? StartDuration      { get; set; }
    public TimeSpan? StopDuration       { get; set; }
    public TimeSpan? DisposeDuration    { get; set; }
    public TimeSpan? TotalDuration      { get; set; }
}
```

`PluginRuntime` changes from `sealed record` to `sealed class` because record equality uses structural comparison on all properties; a mutable runtime object with assembly references, exceptions, and service providers makes no sense as a record.

Also: `PluginDescriptor` needs `StartupOrder` for the sorting. Add it as `init` property:

In `src/MSOSync.Plugin/Models/PluginDescriptor.cs`, add:

```csharp
public int StartupOrder { get; init; } = 1000;
```

And update `PluginLoader.BuildDescriptor` to populate it:

```csharp
StartupOrder = manifest.StartupOrder,
```

Also add the new log event IDs to `PluginLogEvents.cs`:

```csharp
public static readonly EventId PluginInitialized = new(1006, "PluginInitialized");
public static readonly EventId PluginStarted     = new(1007, "PluginStarted");
public static readonly EventId PluginStopped     = new(1008, "PluginStopped");
public static readonly EventId PluginTimeout     = new(1009, "PluginTimeout");
public static readonly EventId PluginDisposed    = new(1010, "PluginDisposed");
```

And add remaining `PluginHostOptions` timeout fields (DefaultTimeoutSeconds, per-phase overrides, DisposeTimeoutSeconds):

```csharp
public sealed class PluginHostOptions
{
    public string PluginsPath              { get; set; } = "plugins";
    public string HostVersion              { get; set; } = "1.0.0";
    public int    DefaultTimeoutSeconds    { get; set; } = 30;
    public int?   InitializeTimeoutSeconds { get; set; }
    public int?   StartTimeoutSeconds      { get; set; }
    public int?   StopTimeoutSeconds       { get; set; }
    public int?   DisposeTimeoutSeconds    { get; set; }
    public string SupportedSdkMajorVersion { get; set; } = "1";
    public string SupportedApiVersion      { get; set; } = "1";
    public long   MaxPluginConfigSizeBytes { get; set; } = 1_048_576;
    public int    MaxPluginCount           { get; set; } = 100;
    public long   MaxManifestSizeBytes     { get; set; } = 65_536;
}
```

- [ ] **Step 3: Write `tests/MSOSync.PluginTests/Lifecycle/PluginLifecycleManagerTests.cs`**

```csharp
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
```

- [ ] **Step 4: Run the lifecycle tests**

```powershell
dotnet test tests\MSOSync.PluginTests --filter "PluginLifecycleManagerTests" -v minimal
```

Expected: All tests pass (13 tests).

- [ ] **Step 5: Run the full PluginTests suite to check for regressions**

```powershell
dotnet test tests\MSOSync.PluginTests -v minimal
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src\MSOSync.Plugin\Lifecycle\PluginRuntimeState.cs `
        src\MSOSync.Plugin\Lifecycle\PluginLifecycleManager.cs `
        src\MSOSync.Plugin\Models\PluginRuntime.cs `
        src\MSOSync.Plugin\Models\PluginDescriptor.cs `
        src\MSOSync.Plugin\Models\PluginHostOptions.cs `
        src\MSOSync.Plugin\Loading\PluginLogEvents.cs `
        src\MSOSync.Plugin\Loading\PluginLoader.cs `
        tests\MSOSync.PluginTests\Lifecycle\PluginLifecycleManagerTests.cs
git commit -m "feat(14B-7): PluginLifecycleManager — InitializeAll/StartAll/StopAll/DisposeAll, timeout, failure isolation, startup ordering"
```
