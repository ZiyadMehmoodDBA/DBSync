# Epic 14B — Task 8: Full Wiring

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Wire all 14B components together. Add `PluginRuntimeManager`. Extend `PluginHost` to delegate to `PluginRuntimeManager`. Update `PluginStatus` enum. Extend `PluginDescriptor` with lifecycle metrics. Update `PluginHealthCheck`. Update `PluginController` DTOs. Update `PluginRegistry` DI registration in `Program.cs`. Update existing integration tests that assert `"Loaded"` status (plugins now reach `"Running"`).

**Architecture:** `PluginRuntimeManager` is the thin orchestrator: calls `Loader → Activator → LifecycleManager` in sequence. `PluginHost` (IHostedService) delegates entirely to `PluginRuntimeManager`. `PluginSummaryDto.Loaded` now counts `Running` plugins (the healthy terminal state in 14B).

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core

## Global Constraints

- `PluginStatus` values after this task: `Loaded, Initialized, Running, Stopped, Disabled, Failed`
- `PluginRegistry` DI: register as concrete singleton first, then forward to `IPluginRegistry`. `PluginActivator` and `PluginLifecycleManager` both take `PluginRegistry` directly.
- Existing integration test that checks `status == "Loaded"` must be updated to `"Running"`
- Existing `PluginSummaryDto.Loaded` count: change to count `Running` state (not `Loaded` state)
- `PluginDto` maps: `InitializeDurationMs`, `StartDurationMs`, `TotalDurationMs`, `InitializedAt`, `StartedAt` from `PluginDescriptor`
- `PluginLifecycleManager.BeginPhaseScope` wired properly via ILogger structured scope
- `TreatWarningsAsErrors=true`

## Files

**Modify:**
- `src/MSOSync.Plugin/Models/PluginStatus.cs` — new enum values
- `src/MSOSync.Plugin/Models/PluginDescriptor.cs` — add lifecycle metric fields
- `src/MSOSync.Plugin/Diagnostics/PluginHealthCheck.cs` — update status mapping
- `src/MSOSync.Plugin/Hosting/PluginHost.cs` — delegate to PluginRuntimeManager
- `src/MSOSync.Plugin/Loading/PluginLoader.cs` — propagate SdkCompatibility check + descriptor status updates from activator
- `src/MSOSync.Api/Controllers/PluginController.cs` — extend PluginDto, update ToDto(), update summary count
- `src/MSOSync.App/Program.cs` — update PluginRegistry/PluginActivator/PluginLifecycleManager/PluginRuntimeManager DI registration
- `tests/MSOSync.IntegrationTests/Plugins/PluginControllerTests.cs` — update status assertion from "Loaded" to "Running"; update summary count field

**Create:**
- `src/MSOSync.Plugin/Hosting/PluginRuntimeManager.cs`

## Interfaces

**Consumes:**
- `PluginActivator.ActivateAsync(string, CancellationToken)` (Task 6)
- `PluginLifecycleManager.InitializeAllAsync/StartAllAsync/StopAllAsync/DisposeAllAsync` (Task 7)
- `PluginRuntime` with all 14B fields (Task 7)
- `PluginRuntimeState` (Task 7)
- `ISdkCompatibilityValidator` (Task 6)

**Produces:**
- `PluginRuntimeManager` — used by `PluginHost`
- Updated `PluginStatus`, `PluginDescriptor`, `PluginDto`
- Updated `PluginHealthCheck` (stopped plugins are healthy)
- Working end-to-end plugin activation in the host

---

- [ ] **Step 1: Update `src/MSOSync.Plugin/Models/PluginStatus.cs`**

Replace the current enum entirely. The old values (`Discovered`, `Validated`) are gone:

```csharp
namespace MSOSync.Plugin.Models;

public enum PluginStatus
{
    Loaded,       // Assembly loaded, awaiting lifecycle start
    Initialized,  // InitializeAsync completed
    Running,      // StartAsync completed — normal operation
    Stopped,      // StopAsync completed
    Disabled,
    Failed
}

public enum PluginLoadOutcome { Success, Skipped, Disabled, Failed }
```

- [ ] **Step 2: Update `src/MSOSync.Plugin/Models/PluginDescriptor.cs`**

Add lifecycle metric fields (all nullable since they're only set after activation):

```csharp
namespace MSOSync.Plugin.Models;

public sealed record PluginDescriptor
{
    public string       PluginId          { get; init; } = null!;
    public string       Name              { get; init; } = null!;
    public string       Version           { get; init; } = null!;
    public PluginStatus Status            { get; set; }
    public string?      ErrorMessage      { get; set; }
    public string?      FailureStage      { get; init; }
    public int          StartupOrder      { get; init; } = 1000;
    public DateTime     LoadedAt          { get; init; }
    public long         LoadDurationMs    { get; init; }
    public string       HostCompatibility { get; init; } = "Compatible";
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
    public PluginManifest? Manifest        { get; init; }

    // 14B lifecycle metrics (set by PluginLifecycleManager after each phase)
    public long?     InitializeDurationMs { get; set; }
    public long?     StartDurationMs      { get; set; }
    public long?     TotalDurationMs      { get; set; }
    public DateTime? InitializedAt        { get; set; }
    public DateTime? StartedAt            { get; set; }
}
```

- [ ] **Step 3: Update `PluginLifecycleManager` to propagate metrics to PluginDescriptor**

After each phase success, copy durations and timestamps from `PluginRuntime` to `rt.Descriptor`. Add to `InitializeAllAsync` success block (after `rt.State = PluginRuntimeState.Initialized`):

```csharp
rt.Descriptor.InitializeDurationMs = (long)rt.InitializeDuration!.Value.TotalMilliseconds;
rt.Descriptor.InitializedAt        = rt.InitializedAt;
rt.Descriptor.Status               = PluginStatus.Initialized;
```

Add to `StartAllAsync` success block:

```csharp
rt.Descriptor.StartDurationMs  = (long)rt.StartDuration!.Value.TotalMilliseconds;
rt.Descriptor.TotalDurationMs  = (long)(rt.TotalDuration?.TotalMilliseconds ?? 0);
rt.Descriptor.StartedAt        = rt.StartedAt;
rt.Descriptor.Status           = PluginStatus.Running;
```

Add to `StopAllAsync` success block:

```csharp
rt.Descriptor.Status = PluginStatus.Stopped;
```

Also wire `BeginPhaseScope` properly in `PluginLifecycleManager`:

```csharp
// Replace the stub BeginPhaseScope with:
private IDisposable? BeginPhaseScope(string pluginId, string version, string phase)
    => logger.BeginScope(new Dictionary<string, object>
    {
        ["PluginId"]      = pluginId,
        ["PluginVersion"] = version,
        ["Phase"]         = phase
    });
```

This requires `PluginLifecycleManager` to hold the `ILogger` field (it already does from Task 7). Remove the `static` keyword from `BeginPhaseScope` in Task 7.

- [ ] **Step 4: Create `src/MSOSync.Plugin/Hosting/PluginRuntimeManager.cs`**

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Hosting;

internal sealed class PluginRuntimeManager(
    IPluginLoader                loader,
    PluginActivator              activator,
    PluginLifecycleManager       lifecycle,
    IOptions<PluginHostOptions>  options,
    ILogger<PluginRuntimeManager> logger)
{
    public long LoadElapsedMs       { get; private set; }
    public long InitializeElapsedMs { get; private set; }
    public long StartElapsedMs      { get; private set; }
    public long TotalElapsedMs      { get; private set; }

    public async Task LoadAndActivateAsync(CancellationToken ct)
    {
        var sw      = Stopwatch.StartNew();
        var results = await loader.LoadAllAsync(options.Value.PluginsPath, ct);
        LoadElapsedMs = sw.ElapsedMilliseconds;

        foreach (var r in results.Where(r => r.Outcome == PluginLoadOutcome.Success))
        {
            await activator.ActivateAsync(r.PluginId, ct);
        }
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await lifecycle.InitializeAllAsync(ct);
        InitializeElapsedMs = sw.ElapsedMilliseconds;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await lifecycle.StartAllAsync(ct);
        StartElapsedMs = sw.ElapsedMilliseconds;
    }

    public async Task StopAsync(CancellationToken ct)
        => await lifecycle.StopAllAsync(ct);

    public async Task DisposeAsync(CancellationToken ct)
        => await lifecycle.DisposeAllAsync(ct);
}
```

- [ ] **Step 5: Rewrite `src/MSOSync.Plugin/Hosting/PluginHost.cs`**

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
    PluginRuntimeManager        runtimeManager,
    IPluginRegistry             registry,
    IPluginLoader               loader,
    IOptions<PluginHostOptions> pluginOptions,
    ILogger<PluginHost>         logger) : IHostedService, IPluginHost
{
    public bool      IsStarted         { get; private set; }
    public DateTime? StartedAt         { get; private set; }
    public long      StartupDurationMs { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        try
        {
            await runtimeManager.LoadAndActivateAsync(cancellationToken);
            await runtimeManager.InitializeAsync(cancellationToken);
            await runtimeManager.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during plugin host startup");
        }

        registry.MarkInitialized();
        total.Stop();

        StartedAt         = DateTime.UtcNow;
        StartupDurationMs = total.ElapsedMilliseconds;
        IsStarted         = true;

        var all        = registry.GetAll();
        var discovered = all.Count;
        var running    = all.Count(p => p.Status == PluginStatus.Running);
        var initialized= all.Count(p => p.Status == PluginStatus.Initialized);
        var failed     = all.Count(p => p.Status == PluginStatus.Failed);
        var disabled   = all.Count(p => p.Status == PluginStatus.Disabled);

        logger.Log(LogLevel.Information, PluginLogEvents.PluginStartupSummary,
            "Plugin host started. Discovered={D} Running={R} Initialized={I} Failed={F} Disabled={Dis} " +
            "LoadElapsed={LE}ms InitializeElapsed={IE}ms StartElapsed={SE}ms TotalElapsed={TE}ms",
            discovered, running, initialized, failed, disabled,
            runtimeManager.LoadElapsedMs, runtimeManager.InitializeElapsedMs,
            runtimeManager.StartElapsedMs, total.ElapsedMilliseconds);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await runtimeManager.StopAsync(cancellationToken);
            await runtimeManager.DisposeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during plugin host shutdown");
        }

        foreach (var ctx in loader.LoadContexts)
        {
            try { ctx.Unload(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error unloading plugin AssemblyLoadContext");
            }
        }
    }
}
```

- [ ] **Step 6: Update `src/MSOSync.Plugin/Diagnostics/PluginHealthCheck.cs`**

`Stopped` is always healthy (host-initiated shutdown). Only `Failed` is Degraded:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Diagnostics;

public sealed class PluginHealthCheck(IPluginRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!registry.IsInitialized)
            return Task.FromResult(HealthCheckResult.Unhealthy("Plugin host not yet started"));

        var enabledPlugins = registry.GetAll()
            .Where(p => p.Status != PluginStatus.Disabled)
            .ToList();

        if (enabledPlugins.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No enabled plugins"));

        // Only Failed state is unhealthy. Stopped is always host-initiated (healthy).
        var failed = enabledPlugins
            .Where(p => p.Status == PluginStatus.Failed)
            .ToList();

        if (failed.Count > 0)
        {
            var details = string.Join(", ", failed.Select(f => $"{f.PluginId} ({f.ErrorMessage})"));
            return Task.FromResult(HealthCheckResult.Degraded($"Failed plugins: {details}"));
        }

        return Task.FromResult(
            HealthCheckResult.Healthy($"{enabledPlugins.Count} plugin(s) active"));
    }
}
```

- [ ] **Step 7: Update `src/MSOSync.Api/Controllers/PluginController.cs`**

Extend `PluginDto` with lifecycle metrics. Update `ToDto()`. Update summary `Loaded` count to count `Running` plugins:

```csharp
// In PluginController, update ToDto:
private static PluginDto ToDto(PluginDescriptor p) => new()
{
    PluginId              = p.PluginId,
    Name                  = p.Name,
    Version               = p.Version,
    Status                = p.Status.ToString(),
    LoadDurationMs        = p.LoadDurationMs,
    InitializeDurationMs  = p.InitializeDurationMs,
    StartDurationMs       = p.StartDurationMs,
    TotalDurationMs       = p.TotalDurationMs,
    LoadedAt              = p.LoadedAt,
    InitializedAt         = p.InitializedAt,
    StartedAt             = p.StartedAt,
    LastError             = p.ErrorMessage,
    FailureStage          = p.FailureStage,
    HostCompatibility     = p.HostCompatibility,
    Capabilities          = p.Capabilities,
    Permissions           = p.Permissions,
    Dependencies          = p.Dependencies,
};

// In GetSummary(), change:
//   Loaded = all.Count(p => p.Status == PluginStatus.Loaded),
// to:
//   Loaded = all.Count(p => p.Status == PluginStatus.Running),
```

Update `PluginDto` class definition:

```csharp
public sealed class PluginDto
{
    public string    PluginId              { get; init; } = null!;
    public string    Name                  { get; init; } = null!;
    public string    Version               { get; init; } = null!;
    public string    Status                { get; init; } = null!;
    public long      LoadDurationMs        { get; init; }
    public long?     InitializeDurationMs  { get; init; }
    public long?     StartDurationMs       { get; init; }
    public long?     TotalDurationMs       { get; init; }
    public DateTime  LoadedAt              { get; init; }
    public DateTime? InitializedAt         { get; init; }
    public DateTime? StartedAt             { get; init; }
    public string?   LastError             { get; init; }
    public string?   FailureStage          { get; init; }
    public string    HostCompatibility     { get; init; } = null!;
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
}
```

- [ ] **Step 8: Update `src/MSOSync.App/Program.cs` — DI registrations**

Find the existing `// --- Epic 14A: Plugin Host ---` block and replace:

```csharp
// --- Epic 14B: Plugin Host (updated) ---
builder.Services.Configure<MSOSync.Plugin.Models.PluginHostOptions>(opts =>
{
    opts.PluginsPath = builder.Configuration["PluginHost:PluginsPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    opts.HostVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
});

// Register PluginRegistry as concrete first so PluginActivator and PluginRuntimeManager
// can inject it directly (for internal runtime access beyond the public IPluginRegistry)
builder.Services.AddSingleton<MSOSync.Plugin.Registry.PluginRegistry>();
builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginRegistry>(sp =>
    sp.GetRequiredService<MSOSync.Plugin.Registry.PluginRegistry>());

builder.Services.AddScoped<MSOSync.Plugin.Abstractions.IPluginStore,
    MSOSync.Persistence.Stores.PluginStore>();
builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginLoader,
    MSOSync.Plugin.Loading.PluginLoader>();
builder.Services.AddSingleton<MSOSync.Plugin.Lifecycle.ISdkCompatibilityValidator,
    MSOSync.Plugin.Lifecycle.SdkCompatibilityValidator>();
builder.Services.AddSingleton<MSOSync.Plugin.Lifecycle.PluginActivator>();
builder.Services.AddSingleton<MSOSync.Plugin.Lifecycle.PluginLifecycleManager>();
builder.Services.AddSingleton<MSOSync.Plugin.Hosting.PluginRuntimeManager>();
builder.Services.AddSingleton<MSOSync.Plugin.Hosting.PluginHost>();
builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginHost>(sp =>
    sp.GetRequiredService<MSOSync.Plugin.Hosting.PluginHost>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<MSOSync.Plugin.Hosting.PluginHost>());
```

Health check (should already be present from 14A Task 6 — verify it's there):

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("workers")
    .AddCheck<MSOSync.Plugin.Diagnostics.PluginHealthCheck>("plugins");
```

Also update `PluginsFixture` in the integration tests to match this registration pattern:

- [ ] **Step 9: Update `tests/MSOSync.IntegrationTests/Plugins/PluginsFixture.cs`**

Replace the plugin host wiring block to match the new registration:

```csharp
// Plugin host wiring (14B)
testBuilder.Services.Configure<PluginHostOptions>(opts =>
{
    opts.PluginsPath = TestPluginsPath;
    opts.HostVersion = "14.0.0";
});
testBuilder.Services.AddSingleton<PluginRegistry>();
testBuilder.Services.AddSingleton<IPluginRegistry>(sp =>
    sp.GetRequiredService<PluginRegistry>());
testBuilder.Services.AddScoped<IPluginStore, PluginStore>();
testBuilder.Services.AddSingleton<IPluginLoader, PluginLoader>();
testBuilder.Services.AddSingleton<ISdkCompatibilityValidator, SdkCompatibilityValidator>();
testBuilder.Services.AddSingleton<PluginActivator>();
testBuilder.Services.AddSingleton<PluginLifecycleManager>();
testBuilder.Services.AddSingleton<PluginRuntimeManager>();
testBuilder.Services.AddSingleton<PluginHost>();
testBuilder.Services.AddSingleton<IPluginHost>(sp =>
    sp.GetRequiredService<PluginHost>());
testBuilder.Services.AddHostedService(sp =>
    sp.GetRequiredService<PluginHost>());
testBuilder.Services.AddHealthChecks()
    .AddCheck<PluginHealthCheck>("plugins");
```

Add the missing using directives at the top:

```csharp
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Hosting;
```

- [ ] **Step 10: Update `tests/MSOSync.IntegrationTests/Plugins/PluginControllerTests.cs`**

Find the test that asserts `status == "Loaded"`:

```csharp
// OLD:
body.GetProperty("status").GetString().Should().Be("Loaded");

// NEW (14B: plugin goes through full lifecycle, ends in Running):
body.GetProperty("status").GetString().Should().Be("Running");
```

Find the `GetPluginSummary_ReturnsCorrectCounts` test. The `loaded` field now counts `Running` plugins:

```csharp
// No change needed to the test assertion itself — it already checks:
body.GetProperty("loaded").GetInt32().Should().BeGreaterThanOrEqualTo(1);
// This still passes since the "loaded" field in PluginSummaryDto now = Running count, and the TestPlugin is Running.
```

Also update `GetPlugins_AsAdmin_Returns200WithTestPlugin` — the TestPlugin should now be `Running`:

```csharp
// No change needed to this test (it only checks presence, not status)
```

- [ ] **Step 11: Update existing `PluginHostTests.cs` for the new PluginHost constructor**

`PluginHost` now takes `PluginRuntimeManager` instead of just `IPluginLoader` + `IPluginRegistry` directly. Update `MakeHost`:

```csharp
private static PluginHost MakeHost(
    PluginRuntimeManager? runtimeManager = null,
    IPluginRegistry? registry = null,
    IPluginLoader? loader = null)
{
    if (runtimeManager == null)
    {
        var loaderMock = new Mock<IPluginLoader>();
        loaderMock.Setup(l => l.LoadContexts).Returns([]);
        loaderMock.Setup(l => l.LoadAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([]);
        loader = loaderMock.Object;

        var registry2   = new PluginRegistry();
        var activator   = /* mock */ null!;  // PluginActivator needs all deps — use a stub PluginRuntimeManager
        // Simplest: mock PluginRuntimeManager directly
        var rmMock = new Mock<PluginRuntimeManager>();
        rmMock.Setup(rm => rm.LoadAndActivateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        rmMock.Setup(rm => rm.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        rmMock.Setup(rm => rm.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        rmMock.Setup(rm => rm.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        rmMock.Setup(rm => rm.DisposeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        runtimeManager = rmMock.Object;
    }
    registry ??= Mock.Of<IPluginRegistry>();
    loader ??= Mock.Of<IPluginLoader>(l => l.LoadContexts == (IReadOnlyList<AssemblyLoadContext>)new List<AssemblyLoadContext>());

    return new PluginHost(
        runtimeManager, registry, loader,
        Options.Create(new PluginHostOptions { PluginsPath = "test", HostVersion = "14.0.0" }),
        NullLogger<PluginHost>.Instance);
}
```

Note: `PluginRuntimeManager` is a concrete class, not an interface — Moq cannot mock it unless it's non-sealed or we add an interface. Simplest solution: **create `IPluginRuntimeManager` interface** with the four async methods, have `PluginRuntimeManager` implement it, and change `PluginHost` to take `IPluginRuntimeManager`:

```csharp
// src/MSOSync.Plugin/Hosting/IPluginRuntimeManager.cs (new file)
internal interface IPluginRuntimeManager
{
    long LoadElapsedMs       { get; }
    long InitializeElapsedMs { get; }
    long StartElapsedMs      { get; }
    Task LoadAndActivateAsync(CancellationToken ct);
    Task InitializeAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task DisposeAsync(CancellationToken ct);
}
```

Update `PluginRuntimeManager` to implement `IPluginRuntimeManager`. Update `PluginHost` to take `IPluginRuntimeManager`. Then `PluginHostTests` mocks `IPluginRuntimeManager`.

Program.cs doesn't need to change — `AddSingleton<PluginRuntimeManager>()` still registers the concrete, and `PluginHost` takes `IPluginRuntimeManager` (resolved from the same concrete via:)

```csharp
builder.Services.AddSingleton<IPluginRuntimeManager>(sp =>
    sp.GetRequiredService<PluginRuntimeManager>());
```

Add this line to Program.cs after the `AddSingleton<PluginRuntimeManager>()` line.

- [ ] **Step 12: Build the full solution**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: `Build succeeded.` 0 errors, 0 warnings. Fix any compilation errors.

Common issues to watch for:
- Removed `PluginStatus.Discovered` and `PluginStatus.Validated` — any code that referenced them must be updated. Search: `grep -r "PluginStatus\.\(Discovered\|Validated\)" src/`
- `PluginRuntime` changed from record to class — ensure `PluginRegistry.Register()` still works (it creates `new PluginRuntime { Descriptor = descriptor }` — this still works with a class)

- [ ] **Step 13: Run all PluginTests unit tests**

```powershell
dotnet test tests\MSOSync.PluginTests -v minimal
```

Expected: All tests pass. Update any tests that still reference old `PluginStatus` values.

- [ ] **Step 14: Run PluginHealthCheckTests to verify updated mapping**

```powershell
dotnet test tests\MSOSync.PluginTests --filter "PluginHealthCheckTests" -v minimal
```

The existing 5 tests reference `PluginStatus.Loaded` in the healthy-state tests. Update them:
- `CheckHealth_AllLoaded_ReturnsHealthy` → use `PluginStatus.Running` instead of `PluginStatus.Loaded`
- `CheckHealth_DisabledExcludedFromDegraded` → use `PluginStatus.Running` for the non-disabled one

- [ ] **Step 15: Commit**

```powershell
git add src\MSOSync.Plugin\ `
        src\MSOSync.Api\Controllers\PluginController.cs `
        src\MSOSync.App\Program.cs `
        tests\MSOSync.IntegrationTests\Plugins\ `
        tests\MSOSync.PluginTests\
git commit -m "feat(14B-8): full wiring — PluginRuntimeManager, updated PluginHost, PluginStatus, PluginDescriptor metrics, PluginDto, HealthCheck, DI registrations"
```
