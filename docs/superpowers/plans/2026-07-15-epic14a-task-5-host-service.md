# Epic 14A — Task 5: PluginHost (IHostedService)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement `IPluginHost` interface and `PluginHost` (IHostedService). `StartAsync` runs the 9-stage loader, marks registry initialized, and logs the startup summary. `StopAsync` disposes all `AssemblyLoadContext`s collected by the loader.

**Architecture:** `PluginHost` is a singleton `IHostedService`. It injects `IPluginLoader`, `IPluginRegistry`, and `IOptions<PluginHostOptions>`. No unit test for startup (covered in Task 9 integration tests). Add a smoke test that verifies startup doesn't throw when `plugins/` directory is missing.

**Tech Stack:** C# 13 / .NET 9 / xUnit + FluentAssertions + Moq

## Global Constraints

- `PluginHost` must never throw in `StartAsync` — all plugin failures are logged, not rethrown
- Uses `PluginHostOptions.PluginsPath` (Task 4) and `IPluginLoader`/`IPluginRegistry` (Task 4)
- Logging via `PluginLogEvents.PluginStartupSummary` (EventId 1005)
- `StopAsync` calls `ctx.Unload()` on each `AssemblyLoadContext` in `loader.LoadContexts`

## Files

**Create:**
- `src/MSOSync.Plugin/Abstractions/IPluginHost.cs`
- `src/MSOSync.Plugin/Hosting/PluginHost.cs`
- `tests/MSOSync.PluginTests/Hosting/PluginHostTests.cs`

## Interfaces

**Consumes:**
- `IPluginLoader` + `LoadContexts` property (Task 4)
- `IPluginRegistry.MarkInitialized()` (Task 3)
- `PluginHostOptions` (Task 4)
- `PluginLogEvents.PluginStartupSummary` (Task 2)
- `PluginLoadOutcome` (Task 4)

**Produces:**
- `IPluginHost` interface (consumed by Task 7 DI wiring)
- `PluginHost` class (consumed by Task 7 DI wiring)

---

- [ ] **Step 1: Create `src/MSOSync.Plugin/Abstractions/IPluginHost.cs`**

```csharp
namespace MSOSync.Plugin.Abstractions;

public interface IPluginHost
{
    bool IsStarted { get; }
    DateTime? StartedAt { get; }
    long StartupDurationMs { get; }
}
```

- [ ] **Step 2: Create `src/MSOSync.Plugin/Hosting/PluginHost.cs`**

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Hosting;

public sealed class PluginHost(
    IPluginLoader               loader,
    IPluginRegistry             registry,
    IOptions<PluginHostOptions> pluginOptions,
    ILogger<PluginHost>         logger) : IHostedService, IPluginHost
{
    public bool     IsStarted        { get; private set; }
    public DateTime? StartedAt       { get; private set; }
    public long     StartupDurationMs { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var sw          = Stopwatch.StartNew();
        var pluginsPath = pluginOptions.Value.PluginsPath;

        IReadOnlyList<Models.PluginLoadResult> results;
        try
        {
            results = await loader.LoadAllAsync(pluginsPath, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during plugin host startup");
            results = [];
        }

        registry.MarkInitialized();
        sw.Stop();

        StartedAt         = DateTime.UtcNow;
        StartupDurationMs = sw.ElapsedMilliseconds;
        IsStarted         = true;

        var total    = results.Count;
        var loaded   = results.Count(r => r.Outcome == PluginLoadOutcome.Success);
        var disabled = results.Count(r => r.Outcome == PluginLoadOutcome.Disabled);
        var failed   = results.Count(r => r.Outcome == PluginLoadOutcome.Failed);

        logger.Log(LogLevel.Information, PluginLogEvents.PluginStartupSummary,
            "Plugin host started in {Ms}ms. Discovered={Total} Loaded={Loaded} Disabled={Disabled} Failed={Failed}",
            sw.ElapsedMilliseconds, total, loaded, disabled, failed);

        foreach (var f in results.Where(r => r.Outcome == PluginLoadOutcome.Failed))
        {
            logger.Log(LogLevel.Warning, PluginLogEvents.PluginFailed,
                "Plugin {Id} failed at stage {Stage}: {Error}",
                f.PluginId, f.FailureStage, f.ErrorMessage);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var ctx in loader.LoadContexts)
        {
            try { ctx.Unload(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error unloading plugin context");
            }
        }
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Create `tests/MSOSync.PluginTests/Hosting/PluginHostTests.cs`**

```csharp
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
        loader ??= Mock.Of<IPluginLoader>(l =>
            l.LoadContexts == (IReadOnlyList<AssemblyLoadContext>)[] &&
            l.LoadAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult<IReadOnlyList<PluginLoadResult>>([]));

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

    [Fact]
    public async Task StopAsync_UnloadsLoadContexts()
    {
        var ctx  = new Mock<AssemblyLoadContext>(false);
        var loader = new Mock<IPluginLoader>();
        loader.Setup(l => l.LoadContexts)
              .Returns(new List<AssemblyLoadContext> { ctx.Object });
        loader.Setup(l => l.LoadAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);

        var host = MakeHost(loader: loader.Object);
        await host.StartAsync(default);
        await host.StopAsync(default);

        ctx.Verify(c => c.Unload(), Times.Once);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/MSOSync.PluginTests --filter "PluginHostTests" -v minimal
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/MSOSync.Plugin/Abstractions/IPluginHost.cs src/MSOSync.Plugin/Hosting/PluginHost.cs tests/MSOSync.PluginTests/Hosting/PluginHostTests.cs
git commit -m "feat(14A-5): PluginHost IHostedService with startup summary logging and context cleanup"
```
