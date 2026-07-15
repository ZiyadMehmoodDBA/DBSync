# Epic 14B: Plugin Runtime + SDK Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable a developer to build a plugin using `MSOSync.Sdk`, drop it into `plugins/`, and have the host load, initialize, start, stop, and dispose it safely in isolation.

**Architecture:** New `MSOSync.Sdk` project (zero dependencies) defines the public plugin-author contracts. `MSOSync.Plugin` extends its host runtime with bridge adapters, a `PluginActivator` pipeline, and a `PluginLifecycleManager` that drives `IPlugin` through `InitializeAsync → StartAsync → StopAsync → DisposeAsync`. Per-plugin sub-containers isolate service access. `MSOSync.Plugin.IntegrationTests` validates the full lifecycle with a real DLL.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / xUnit + FluentAssertions + Moq / React 19 (TypeScript)

## Global Constraints

- `MSOSync.Sdk` must have zero NuGet dependencies and zero project references — it builds in complete isolation
- `MSOSync.Plugin` references `MSOSync.Sdk` and `MSOSync.Common` only
- `MSOSync.TestPlugin` must reference only `MSOSync.Sdk` (not `MSOSync.Plugin` or any host project)
- All projects: `net9.0`, `LangVersion 13.0`, `Nullable enable`, `TreatWarningsAsErrors true` (from `Directory.Build.props`)
- Package versions managed centrally in `Directory.Packages.props` — no explicit versions in individual `.csproj` files
- `PluginStatus` enum values in 14B: `Loaded, Initialized, Running, Stopped, Disabled, Failed`
- `PluginRuntimeState` enum is `internal` — never exposed in REST API
- `CompatibilityResult` enum values: `Compatible, Warning, Incompatible`
- Log event IDs 1006–1010: `PluginInitialized, PluginStarted, PluginStopped, PluginTimeout, PluginDisposed`
- Plugin parameterless constructor required in 14B; constructor injection deferred to 14C
- `IPluginContext` is created once per plugin and never mutated; same instance passed to `InitializeAsync`
- Config priority: `IConfiguration["Plugins:{pluginId}:*"]` wins over `plugin.config.json`; malformed config file is non-fatal
- Manifest JSON field `manifestVersion` (int, default 1), `sdkVersion` (string, e.g. `"1.0"`), `apiVersion` (string, e.g. `"1"`), `startupOrder` (int, default 1000)
- Startup ordering: ascending `startupOrder` then ascending `PluginId` for ties; shutdown is reverse
- Host SDK 1.x accepts plugin `sdkVersion` 1.x; `apiVersion` "1" accepted; mismatch → `Failed(SdkCompatibility)`
- Folder hardening: unknown subfolders → warning; missing DLL → failure (already enforced by validator); symlinks outside plugins dir → skip+warning; paths normalized with `Path.GetFullPath`; `MaxPluginCount` (default 100) caps discovery; `MaxManifestSizeBytes` (default 65536); `MaxPluginConfigSizeBytes` (default 1048576)
- Health: only `Failed` status → Degraded; `Stopped` is always healthy (host-initiated)
- `PluginDto` gains: `initializeDurationMs`, `startDurationMs`, `totalDurationMs`, `initializedAt?`, `startedAt?`
- Integration test status assertion: plugins reach `Running` state (not `Loaded`) after 14B

---

## Tasks

| # | Task | File |
|---|------|------|
| 1 | `MSOSync.Sdk` project — all interfaces, enums, `PluginBase`, `PluginMetadata` | [task-1](2026-07-15-epic14b-task-1-sdk-project.md) |
| 2 | `MSOSync.SdkTests` — golden API test, `PluginBase` tests, capability flag tests | [task-2](2026-07-15-epic14b-task-2-sdk-tests.md) |
| 3 | Update `MSOSync.TestPlugin`: implement `IPlugin`, update `plugin.json`, rebuild DLL | [task-3](2026-07-15-epic14b-task-3-test-plugin.md) |
| 4 | Bridge adapters: `PluginLoggerAdapter`, `PluginEnvironmentAdapter`, `PluginServicesAdapter`, `PluginContext` | [task-4](2026-07-15-epic14b-task-4-bridge-adapters.md) |
| 5 | `PluginConfigurationFile` + `PluginConfigurationAdapter` + configuration unit tests | [task-5](2026-07-15-epic14b-task-5-configuration.md) |
| 6 | `ISdkCompatibilityValidator` + `SdkCompatibilityValidator`; extend `PluginManifest`/`PluginManifestValidator`; `PluginActivator` + tests | [task-6](2026-07-15-epic14b-task-6-activator.md) |
| 7 | `PluginRuntimeState` + `PluginLifecycleManager` + table-driven lifecycle tests | [task-7](2026-07-15-epic14b-task-7-lifecycle.md) |
| 8 | Full wiring: extend `PluginRuntime`/`PluginStatus`/`PluginDescriptor`/`PluginHostOptions`/`PluginLogEvents`; add `PluginRuntimeManager`; update `PluginRegistry`, `PluginLoader`, `PluginHost`, `PluginHealthCheck`, `PluginController` DTOs, `Program.cs`, existing integration tests | [task-8](2026-07-15-epic14b-task-8-wiring.md) |
| 9 | `MSOSync.Plugin.IntegrationTests` (10 tests) + frontend `PluginStatusBadge` update | [task-9](2026-07-15-epic14b-task-9-integration.md) |

---

## Key Interfaces Cross-Reference

```csharp
// MSOSync.Sdk/Abstractions/IPlugin.cs
public interface IPlugin : IAsyncDisposable
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

// MSOSync.Sdk/Abstractions/IPluginContext.cs
public interface IPluginContext
{
    PluginMetadata       Metadata      { get; }
    IPluginLogger        Logger        { get; }
    IPluginConfiguration Configuration { get; }
    IPluginServices      Services      { get; }
    IPluginEnvironment   Environment   { get; }
}

// MSOSync.Sdk/Abstractions/IPluginConfiguration.cs
public interface IPluginConfiguration
{
    T?                          GetValue<T>(string key);
    T                           GetValue<T>(string key, T defaultValue);
    IPluginConfiguration        GetSection(string sectionName);
    IReadOnlyCollection<string> Keys  { get; }
    bool                        Exists(string key);
}

// MSOSync.Sdk/Abstractions/IPluginServices.cs
public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}

// MSOSync.Sdk/Abstractions/IPluginLogger.cs
public interface IPluginLogger
{
    void        LogDebug(string message, params object?[] args);
    void        LogInformation(string message, params object?[] args);
    void        LogWarning(string message, params object?[] args);
    void        LogWarning(Exception exception, string message, params object?[] args);
    void        LogError(Exception? exception, string message, params object?[] args);
    void        LogCritical(Exception? exception, string message, params object?[] args);
    IDisposable BeginScope(string name);
}

// MSOSync.Sdk/Abstractions/IPluginEnvironment.cs
public interface IPluginEnvironment
{
    string EnvironmentName { get; }
    bool   IsDevelopment   { get; }
    bool   IsProduction    { get; }
    string HostVersion     { get; }
    string DataDirectory   { get; }
    string PluginDirectory { get; }
}

// MSOSync.Plugin/Lifecycle/ISdkCompatibilityValidator.cs (internal)
internal interface ISdkCompatibilityValidator
{
    CompatibilityResult Validate(PluginManifest manifest, out string? message);
}

public enum CompatibilityResult { Compatible, Warning, Incompatible }

// MSOSync.Plugin/Lifecycle/PluginActivator.cs (internal)
internal sealed class PluginActivator { ... }
// ActivateAsync(string pluginId, CancellationToken ct) → mutates PluginRuntime in registry

// MSOSync.Plugin/Lifecycle/PluginLifecycleManager.cs (internal)
internal sealed class PluginLifecycleManager { ... }
// InitializeAllAsync(CancellationToken ct)
// StartAllAsync(CancellationToken ct)
// StopAllAsync(CancellationToken ct)
// DisposeAllAsync(CancellationToken ct)

// MSOSync.Plugin/Hosting/PluginRuntimeManager.cs (internal)
internal sealed class PluginRuntimeManager { ... }
// LoadAndActivateAsync(string pluginsPath, CancellationToken ct)
// InitializeAsync(CancellationToken ct) → delegates to LifecycleManager
// StartAsync(CancellationToken ct)     → delegates to LifecycleManager
// StopAsync(CancellationToken ct)      → delegates to LifecycleManager
// DisposeAsync(CancellationToken ct)   → delegates to LifecycleManager

// PluginRegistry (extended internal methods, same project only)
internal PluginRuntime? GetRuntime(string pluginId)
internal IReadOnlyList<PluginRuntime> GetAllRuntimes()
```

## Extended `PluginRuntime` (after Task 8)

```csharp
internal sealed class PluginRuntime
{
    // From 14A (changed init→set for Assembly and LoadContext)
    public PluginDescriptor      Descriptor     { get; set; } = null!;
    public Assembly?             Assembly       { get; set; }
    public AssemblyLoadContext?  LoadContext    { get; set; }

    // New in 14B
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

Note: `PluginRuntime` changes from `sealed record` to `sealed class` in Task 8 (records with all-set properties should be classes for clarity, and record equality semantics are wrong here).

## `PluginDescriptor` (new fields in Task 8)

```csharp
// New nullable fields added to existing PluginDescriptor record:
public long?     InitializeDurationMs { get; init; }
public long?     StartDurationMs      { get; init; }
public long?     TotalDurationMs      { get; init; }
public DateTime? InitializedAt        { get; init; }
public DateTime? StartedAt            { get; init; }
```

## `PluginHostOptions` (after Task 8)

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
    public int    MaxPluginCount           { get; set; } = 100;
    public long   MaxManifestSizeBytes     { get; set; } = 65_536;
    public long   MaxPluginConfigSizeBytes { get; set; } = 1_048_576;
}
```

## Progress Ledger

SDD ledger: `.superpowers/sdd/progress-epic14b.md`
