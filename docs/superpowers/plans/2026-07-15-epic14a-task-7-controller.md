# Epic 14A — Task 7: PluginController + DI Wiring

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement `PluginController` with 6 endpoints, wire all plugin services in `Program.cs`, add all required project references, and verify the full solution builds.

**Architecture:** Controller returns 503 when registry not initialized. Enable/Disable always returns `{ "success": true, "restartRequired": true }`. All 6 endpoints are `[Authorize(Policy = "AdminOnly")]`.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / MSOSync.Api / MSOSync.App

## Global Constraints

- `[Authorize(Policy = "AdminOnly")]` at class level
- Route: `api/v1/plugins`
- Returns 503 (StatusCode 503) when `!registry.IsInitialized`
- Returns 404 when plugin ID not found
- Enable/Disable: `restartRequired: true` always in 14A
- `PluginDto` exposes: `pluginId, name, version, status, loadDurationMs, loadedAt, lastError, failureStage, hostCompatibility, capabilities, permissions, dependencies`
- `PluginSummaryDto` exposes: `total, loaded, failed, disabled, startupDurationMs, lastScanAt`
- `PluginActionResult` exposes: `{ "success": true, "restartRequired": true }`

## Files

**Create:**
- `src/MSOSync.Api/Controllers/PluginController.cs`

**Modify:**
- `src/MSOSync.App/Program.cs` — register plugin services + hosted service
- `src/MSOSync.App/MSOSync.App.csproj` — add ref to MSOSync.Plugin
- `src/MSOSync.Api/MSOSync.Api.csproj` — add ref to MSOSync.Plugin
- `src/MSOSync.Persistence/MSOSync.Persistence.csproj` — already has ref (Task 1); verify it's present

## Interfaces

**Consumes:**
- `IPluginRegistry` (Tasks 3, 4)
- `IPluginStore.SetEnabledAsync` (Task 1)
- `IPluginHost` (Task 5)
- `PluginDescriptor` (Task 4)
- `PluginHealthCheck` (Task 6)

**Produces:** REST API endpoints (consumed by Task 8 frontend and Task 9 integration tests)

---

- [ ] **Step 1: Add `MSOSync.Plugin` project reference to `MSOSync.Api.csproj`**

Open `src/MSOSync.Api/MSOSync.Api.csproj`, add inside existing `<ItemGroup>`:

```xml
<ProjectReference Include="..\MSOSync.Plugin\MSOSync.Plugin.csproj" />
```

- [ ] **Step 2: Add `MSOSync.Plugin` project reference to `MSOSync.App.csproj`**

Open `src/MSOSync.App/MSOSync.App.csproj`, add inside existing `<ItemGroup>`:

```xml
<ProjectReference Include="..\MSOSync.Plugin\MSOSync.Plugin.csproj" />
```

- [ ] **Step 3: Create `src/MSOSync.Api/Controllers/PluginController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/plugins")]
[Authorize(Policy = "AdminOnly")]
public sealed class PluginController(
    IPluginRegistry registry,
    IPluginStore    store,
    IPluginHost     pluginHost) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PluginDto>), 200)]
    [ProducesResponseType(503)]
    public IActionResult GetAll()
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new { error = "Plugin host not yet initialized" });

        var dtos = registry.GetAll().Select(ToDto).ToList();
        return Ok(dtos);
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(PluginSummaryDto), 200)]
    [ProducesResponseType(503)]
    public IActionResult GetSummary()
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new { error = "Plugin host not yet initialized" });

        var all = registry.GetAll();
        return Ok(new PluginSummaryDto
        {
            Total              = all.Count,
            Loaded             = all.Count(p => p.Status == PluginStatus.Loaded),
            Failed             = all.Count(p => p.Status == PluginStatus.Failed),
            Disabled           = all.Count(p => p.Status == PluginStatus.Disabled),
            StartupDurationMs  = pluginHost.StartupDurationMs,
            LastScanAt         = pluginHost.StartedAt,
        });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(PluginDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(503)]
    public IActionResult GetById(string id)
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new { error = "Plugin host not yet initialized" });

        var plugin = registry.GetById(id);
        if (plugin == null) return NotFound();
        return Ok(ToDto(plugin));
    }

    [HttpGet("{id}/manifest")]
    [ProducesResponseType(typeof(PluginManifest), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(503)]
    public IActionResult GetManifest(string id)
    {
        if (!registry.IsInitialized)
            return StatusCode(503, new { error = "Plugin host not yet initialized" });

        var plugin = registry.GetById(id);
        if (plugin == null) return NotFound();
        if (plugin.Manifest == null) return NotFound();
        return Ok(plugin.Manifest);
    }

    [HttpPost("{id}/enable")]
    [ProducesResponseType(typeof(PluginActionResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Enable(string id, CancellationToken ct)
    {
        try
        {
            await store.SetEnabledAsync(id, true, ct);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        return Ok(new PluginActionResult(Success: true, RestartRequired: true));
    }

    [HttpPost("{id}/disable")]
    [ProducesResponseType(typeof(PluginActionResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Disable(string id, CancellationToken ct)
    {
        try
        {
            await store.SetEnabledAsync(id, false, ct);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        return Ok(new PluginActionResult(Success: true, RestartRequired: true));
    }

    private static PluginDto ToDto(PluginDescriptor p) => new()
    {
        PluginId          = p.PluginId,
        Name              = p.Name,
        Version           = p.Version,
        Status            = p.Status.ToString(),
        LoadDurationMs    = p.LoadDurationMs,
        LoadedAt          = p.LoadedAt,
        LastError         = p.ErrorMessage,
        FailureStage      = p.FailureStage,
        HostCompatibility = p.HostCompatibility,
        Capabilities      = p.Capabilities,
        Permissions       = p.Permissions,
        Dependencies      = p.Dependencies,
    };
}

// DTO records — defined here to keep the controller self-contained
public sealed class PluginDto
{
    public string   PluginId          { get; init; } = null!;
    public string   Name              { get; init; } = null!;
    public string   Version           { get; init; } = null!;
    public string   Status            { get; init; } = null!;
    public long     LoadDurationMs    { get; init; }
    public DateTime LoadedAt          { get; init; }
    public string?  LastError         { get; init; }
    public string?  FailureStage      { get; init; }
    public string   HostCompatibility { get; init; } = null!;
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
}

public sealed class PluginSummaryDto
{
    public int       Total             { get; init; }
    public int       Loaded            { get; init; }
    public int       Failed            { get; init; }
    public int       Disabled          { get; init; }
    public long      StartupDurationMs { get; init; }
    public DateTime? LastScanAt        { get; init; }
}

public sealed record PluginActionResult(bool Success, bool RestartRequired);
```

- [ ] **Step 4: Register all plugin services in `src/MSOSync.App/Program.cs`**

After the existing `// Export jobs` comment block (around line 116), add a new block:

```csharp
// --- Epic 14A: Plugin Host ---
builder.Services.Configure<MSOSync.Plugin.Models.PluginHostOptions>(opts =>
{
    opts.PluginsPath = builder.Configuration["PluginHost:PluginsPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    opts.HostVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
});
builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginRegistry,
    MSOSync.Plugin.Registry.PluginRegistry>();
builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginStore,
    MSOSync.Persistence.Stores.PluginStore>();
builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginLoader,
    MSOSync.Plugin.Loading.PluginLoader>();
builder.Services.AddSingleton<MSOSync.Plugin.Abstractions.IPluginHost>(sp =>
    sp.GetRequiredService<MSOSync.Plugin.Hosting.PluginHost>());
builder.Services.AddSingleton<MSOSync.Plugin.Hosting.PluginHost>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<MSOSync.Plugin.Hosting.PluginHost>());
```

Also update the health checks chain to add the plugin check (if not already done in Task 6):

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("workers")
    .AddCheck<MSOSync.Plugin.Diagnostics.PluginHealthCheck>("plugins");
```

**Note:** `IPluginStore` is registered as `PluginStore` (scoped in EF terms, but the loader is singleton). `PluginStore` needs `AppDbContext` which is scoped. Fix: make `PluginStore` resolve `AppDbContext` via `IServiceScopeFactory` OR change registration to use a factory. Use factory pattern:

Replace the `IPluginStore` registration with:

```csharp
builder.Services.AddTransient<MSOSync.Plugin.Abstractions.IPluginStore>(sp =>
{
    var db = sp.GetRequiredService<MSOSync.Persistence.AppDbContext>();
    return new MSOSync.Persistence.Stores.PluginStore(db);
});
```

But this won't work for singleton `PluginLoader`. The proper fix: `PluginStore` should be transient/scoped, and `PluginLoader` should use `IServiceScopeFactory` to resolve a store scope per load operation. This is the standard pattern for scoped services used by singletons.

**Revised approach** — update `PluginLoader` constructor to accept `IServiceScopeFactory` instead of `IPluginStore` directly:

In `src/MSOSync.Plugin/Loading/PluginLoader.cs`, replace `IPluginStore store` parameter with `IServiceScopeFactory scopeFactory`:

```csharp
// Replace the IPluginStore injection in PluginLoader with IServiceScopeFactory:
public sealed class PluginLoader(
    IPluginRegistry              registry,
    IServiceScopeFactory         scopeFactory,
    IOptions<PluginHostOptions>  options,
    ILogger<PluginLoader>        logger) : IPluginLoader
```

Then in `LoadAllAsync`, acquire store from scope:

```csharp
await using var scope = scopeFactory.CreateAsyncScope();
var store = scope.ServiceProvider.GetRequiredService<IPluginStore>();
var storeRecords = (await store.GetAllAsync(ct)) ...
```

And in `PersistAsync`, accept `IPluginStore` as a parameter (passed from the scope):

```csharp
private static async Task PersistAsync(IPluginStore store, string pluginId, ...) { ... }
```

Update `LoadPluginAsync` signature to pass `store` through. Also update `PluginLoaderTests.cs` to inject `IServiceScopeFactory` mock.

**Updated `PluginLoader.cs` key changes** (show only the modified constructor and `LoadAllAsync` top):

```csharp
public sealed class PluginLoader(
    IPluginRegistry              registry,
    IServiceScopeFactory         scopeFactory,
    IOptions<PluginHostOptions>  options,
    ILogger<PluginLoader>        logger) : IPluginLoader
{
    public async Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(
        string pluginsPath, CancellationToken ct)
    {
        var results = new List<PluginLoadResult>();
        if (!Directory.Exists(pluginsPath)) return results;

        var dirs = Directory.GetDirectories(pluginsPath)
            .Where(d => File.Exists(Path.Combine(d, "plugin.json")))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Acquire a single scope for the entire startup scan
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPluginStore>();

        var storeRecords = (await store.GetAllAsync(ct))
            .ToDictionary(r => r.PluginId, StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            logger.Log(LogLevel.Debug, PluginLogEvents.PluginDirectoryDiscovered,
                "Discovered plugin directory: {Dir}", dir);
            var result = await LoadPluginAsync(dir, storeRecords, seenIds, store, ct);
            results.Add(result);
        }

        return results;
    }
    // ... rest of methods pass `store` as param
}
```

Update `PluginLoaderTests.MakeLoader` to pass `IServiceScopeFactory`:

```csharp
private PluginLoader MakeLoader(IPluginStore? store = null)
{
    store ??= Mock.Of<IPluginStore>(s =>
        s.GetAllAsync(It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<PluginRecord>>([]));

    var scopeFactory = new Mock<IServiceScopeFactory>();
    var scope        = new Mock<IServiceScope>();
    var provider     = new Mock<IServiceProvider>();

    provider.Setup(p => p.GetService(typeof(IPluginStore))).Returns(store);
    scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
    scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

    return new PluginLoader(
        new PluginRegistry(),
        scopeFactory.Object,
        Options.Create(new PluginHostOptions { PluginsPath = _pluginsRoot, HostVersion = "14.0.0" }),
        NullLogger<PluginLoader>.Instance);
}
```

Also add `Microsoft.Extensions.DependencyInjection` using to `PluginLoader.cs`.

- [ ] **Step 5: Register `IPluginStore` as scoped in `Program.cs`**

```csharp
builder.Services.AddScoped<MSOSync.Plugin.Abstractions.IPluginStore,
    MSOSync.Persistence.Stores.PluginStore>();
```

Also need `IServiceScopeFactory` in `MSOSync.Plugin.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 6: Build entire solution**

```bash
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: Build succeeded, 0 errors. Fix any compilation errors before committing.

- [ ] **Step 7: Run unit tests**

```bash
dotnet test tests/MSOSync.PluginTests -v minimal
```

Expected: All tests pass. Update `PluginLoaderTests` as described in Step 4 if tests break due to `IServiceScopeFactory` change.

- [ ] **Step 8: Commit**

```bash
git add src/MSOSync.Api/Controllers/PluginController.cs src/MSOSync.Api/MSOSync.Api.csproj src/MSOSync.App/MSOSync.App.csproj src/MSOSync.App/Program.cs src/MSOSync.Plugin/MSOSync.Plugin.csproj src/MSOSync.Plugin/Loading/PluginLoader.cs tests/MSOSync.PluginTests/Loading/PluginLoaderTests.cs
git commit -m "feat(14A-7): PluginController (6 endpoints), DI wiring, IServiceScopeFactory in PluginLoader"
```
