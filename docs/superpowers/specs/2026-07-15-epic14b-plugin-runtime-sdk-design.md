# Epic 14B: Plugin Runtime + SDK — Design Specification

**Date:** 2026-07-15
**Status:** Approved
**Scope:** SDK contracts, plugin activation, per-plugin sub-container, lifecycle management.
No extension points (14C). No first-party plugins (14D).

---

## Goal

Enable a developer to build a plugin using `MSOSync.Sdk`, drop it into `plugins/`, and have the host load, initialize, start, stop, and dispose it safely in isolation from the host and from other plugins.

**Completion criteria (all must be true before 14B is done):**
1. `MSOSync.Sdk` builds independently with no references to `MSOSync.Plugin`, `MSOSync.Api`, `MSOSync.App`, or `MSOSync.Persistence`.
2. `MSOSync.TestPlugin` references only `MSOSync.Sdk`.
3. Plugin discovery loads and activates plugins through the full lifecycle.
4. Lifecycle executes `InitializeAsync → StartAsync → StopAsync → DisposeAsync` in order.
5. One plugin failure never prevents other plugins from running.
6. Plugin configuration resolves correctly from layered sources (appsettings wins over file).
7. Health endpoint reports lifecycle-based status (`Failed` → Degraded only).
8. Frontend `PluginStatusBadge` reflects `Running`, `Initialized`, `Stopped` states.
9. All SDK, unit, and integration tests pass.
10. `dotnet build MSOSync.sln` — 0 errors, 0 warnings.

---

## Project Structure

```
src/
├── MSOSync.Sdk                      ← NEW — plugin author API only
├── MSOSync.Common
├── MSOSync.Plugin                   ← EXTENDED — host runtime
├── MSOSync.Persistence
├── MSOSync.Api
└── MSOSync.App

tests/
├── MSOSync.SdkTests                 ← NEW — SDK surface + API compat tests
├── MSOSync.PluginTests              ← EXTENDED — unit tests
├── MSOSync.Plugin.IntegrationTests  ← NEW — full lifecycle integration tests
└── MSOSync.TestPlugin               ← EXTENDED — implements IPlugin
```

### Dependency rules (strict)

```
Third-party plugin author
        │
        ▼
  MSOSync.Sdk              (zero NuGet dependencies)
        ▲
        │
  MSOSync.Plugin           (references MSOSync.Sdk + MSOSync.Common)
        ▲
        │
  MSOSync.App              (references MSOSync.Plugin + MSOSync.Persistence)

MSOSync.Persistence        (references MSOSync.Plugin for IPluginStore — no lifecycle types)
```

**Rule:** Everything in `MSOSync.Sdk` is a public contract. Breaking changes require a major SDK version bump. Every type added to `MSOSync.Sdk` is assumed to be supported long-term.

---

## `plugin.json` Schema Changes

Four new fields (additions only — backward-compatible):

```json
{
  "manifestVersion": 1,
  "id":           "msosync.sqlserver.collector",
  "name":         "SQL Server Collector",
  "version":      "1.0.0",
  "sdkVersion":   "1.0",
  "apiVersion":   "1",
  "startupOrder": 100,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "MSOSync.SqlCollector.dll",
  "entryType":    "MSOSync.SqlCollector.Plugin",
  "author":       "MSOSync",
  "description":  "Collects SQL Server metrics.",
  "permissions":  ["Collectors"],
  "dependencies": [],
  "capabilities": ["Collector"]
}
```

| Field | Required | Default | Notes |
|---|---|---|---|
| `manifestVersion` | no | `1` | Manifest schema version; loader selects parser by this value |
| `sdkVersion` | yes (14B+) | — | Major.minor string, e.g. `"1.0"` — parsed to `Version` internally |
| `apiVersion` | yes (14B+) | — | Integer string, e.g. `"1"` — parsed to `int` internally |
| `startupOrder` | no | `1000` | Ascending = initialize first; ties broken by `PluginId` ascending |

`PluginManifestValidator` validates all four. `ISdkCompatibilityValidator` (host-internal) checks `sdkVersion` and `apiVersion` against host-supported ranges.

**Version parsing rule:** Manifest fields `sdkVersion` and `apiVersion` are JSON strings for forward compatibility. The validator converts them to `System.Version` and `int` immediately after parsing. All internal comparisons use typed values — never raw string comparison.

**SDK compatibility policy:**
- `ISdkCompatibilityValidator.Validate(manifest)` returns `CompatibilityResult`:
  ```csharp
  public enum CompatibilityResult { Compatible, Warning, Incompatible }
  ```
  `Compatible` — plugin loads normally. `Warning` — plugin loads, warning logged (e.g., minor mismatch). `Incompatible` — plugin → `Failed(stage: SdkCompatibility)`.
- Host SDK 1.x: plugin `sdkVersion` 1.x → `Compatible`; 2.x → `Incompatible`.
- `apiVersion` checked independently; future minor increments may → `Warning`.

---

## `MSOSync.Sdk` — Public API

Zero NuGet dependencies. All types are public contracts.

### Folder structure

```
src/MSOSync.Sdk/
├── Abstractions/
│   ├── IPlugin.cs
│   ├── IPluginContext.cs
│   ├── IPluginConfiguration.cs
│   ├── IPluginServices.cs
│   ├── IPluginLogger.cs
│   └── IPluginEnvironment.cs
├── Metadata/
│   └── PluginMetadata.cs
├── Hosting/
│   └── PluginBase.cs
├── Events/               ← namespace reserved for 14C, empty in 14B
└── MSOSync.Sdk.csproj
```

### Interfaces

```csharp
// Abstractions/IPlugin.cs
public interface IPlugin : IAsyncDisposable
{
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

// Abstractions/IPluginContext.cs
public interface IPluginContext
{
    PluginMetadata       Metadata      { get; }
    IPluginLogger        Logger        { get; }
    IPluginConfiguration Configuration { get; }
    IPluginServices      Services      { get; }
    IPluginEnvironment   Environment   { get; }
}

// Abstractions/IPluginConfiguration.cs
public interface IPluginConfiguration
{
    T?                   GetValue<T>(string key);
    T                    GetValue<T>(string key, T defaultValue);
    IPluginConfiguration GetSection(string sectionName);
    IReadOnlyCollection<string> Keys { get; }
    bool                 Exists(string key);
}

// Abstractions/IPluginServices.cs
public interface IPluginServices
{
    T              GetRequiredService<T>() where T : notnull;
    T?             GetService<T>();
    IEnumerable<T> GetServices<T>();
}

// Abstractions/IPluginLogger.cs
public interface IPluginLogger
{
    void LogDebug(string message, params object?[] args);
    void LogInformation(string message, params object?[] args);
    void LogWarning(string message, params object?[] args);
    void LogWarning(Exception exception, string message, params object?[] args);
    void LogError(Exception? exception, string message, params object?[] args);
    void LogCritical(Exception? exception, string message, params object?[] args);
    IDisposable BeginScope(string name);
}

// Abstractions/IPluginEnvironment.cs
public interface IPluginEnvironment
{
    string EnvironmentName { get; }
    bool   IsDevelopment   { get; }
    bool   IsProduction    { get; }
    string HostVersion     { get; }
    string DataDirectory   { get; }
    string PluginDirectory { get; }
}
```

### Models

```csharp
// Metadata/PluginMetadata.cs
public sealed record PluginMetadata
{
    public string PluginId    { get; init; } = null!;
    public string Name        { get; init; } = null!;
    public string Version     { get; init; } = null!;
    public string SdkVersion  { get; init; } = null!;
    public string ApiVersion  { get; init; } = null!;
    public string Author      { get; init; } = null!;
    public string Description { get; init; } = null!;
    public IReadOnlySet<PluginCapability> Capabilities { get; init; } = new HashSet<PluginCapability>();
    public IReadOnlySet<PluginPermission> Permissions  { get; init; } = new HashSet<PluginPermission>();
}

// Metadata/PluginCapability.cs
[Flags]
public enum PluginCapability
{
    None      = 0,
    Collector = 1,
    Transport = 2,
    Operation = 4,
    Router    = 8,
    Health    = 16
}

// Metadata/PluginPermission.cs — extended in 14C as needed
public enum PluginPermission
{
    None       = 0,
    Collectors = 1,
    Transport  = 2,
    Operations = 4
}
```

Manifest `capabilities` and `permissions` strings are mapped to these enums by the host loader. Unknown strings → logged warning, skipped.

### `PluginBase` (optional convenience class)

```csharp
// Hosting/PluginBase.cs
public abstract class PluginBase : IPlugin
{
    protected IPluginContext Context { get; private set; } = null!;

    public virtual Task InitializeAsync(IPluginContext context, CancellationToken ct)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public virtual Task StartAsync(CancellationToken ct)   => Task.CompletedTask;
    public virtual Task StopAsync(CancellationToken ct)    => Task.CompletedTask;
    public virtual ValueTask DisposeAsync()                => ValueTask.CompletedTask;
}
```

Plugin authors override only the methods they need. `Context` is always available after `InitializeAsync`.

---

## Host Runtime — `MSOSync.Plugin` Extensions

### Internal state machine

```csharp
internal enum PluginRuntimeState
{
    Loaded,        // assembly loaded, type verified (14A end state)
    Initializing,  // InitializeAsync in progress
    Initialized,   // InitializeAsync completed
    Starting,      // StartAsync in progress
    Running,       // StartAsync completed — steady state
    Stopping,      // StopAsync in progress
    Stopped,       // StopAsync completed
    Disposing,     // DisposeAsync in progress
    Disposed,      // DisposeAsync completed
    Failed,        // any phase failed
    Disabled       // filtered at stage 4
}
```

### Public `PluginStatus` enum (extended)

```csharp
public enum PluginStatus
{
    Loaded,        // assembly loaded, awaiting lifecycle start
    Initialized,   // InitializeAsync done
    Running,       // StartAsync done — normal operation
    Stopped,       // StopAsync done
    Disabled,
    Failed
}
```

Internal `PluginRuntimeState` maps to public `PluginStatus` for the REST API. Transitional states (`Initializing`, `Starting`, etc.) map to the preceding stable state during the transition.

### Extended `PluginRuntime`

```csharp
internal sealed record PluginRuntime
{
    // from 14A
    public PluginDescriptor      Descriptor     { get; set; } = null!;
    public Assembly?             Assembly       { get; set; }  // populated in 14B
    public AssemblyLoadContext?  LoadContext    { get; set; }  // populated in 14B

    // new in 14B
    public IPlugin?              Instance       { get; set; }
    public IServiceProvider?     PluginServices { get; set; }
    public IPluginContext?       Context        { get; set; }  // created once, never replaced
    public PluginRuntimeState    State          { get; set; } = PluginRuntimeState.Loaded;
    public Exception?            LastException  { get; set; }

    // lifecycle timestamps
    public DateTime? InitializedAt     { get; set; }
    public DateTime? StartedAt         { get; set; }
    public DateTime? StoppedAt         { get; set; }
    public DateTime? DisposedAt        { get; set; }
    public DateTime  LastStateChangeUtc{ get; set; }

    // lifecycle durations (all exposed on PluginDto)
    public TimeSpan? InitializeDuration { get; set; }
    public TimeSpan? StartDuration      { get; set; }
    public TimeSpan? StopDuration       { get; set; }
    public TimeSpan? DisposeDuration    { get; set; }
    public TimeSpan? TotalDuration      { get; set; }  // LoadDuration + InitializeDuration + StartDuration
}
```

### New components in `MSOSync.Plugin`

```
MSOSync.Plugin/
├── Configuration/
│   ├── PluginConfigurationAdapter.cs    ← IPluginConfiguration impl (layered)
│   └── PluginConfigurationFile.cs       ← reads plugin.config.json
├── Runtime/
│   ├── PluginRuntimeState.cs
│   ├── PluginContext.cs                 ← concrete IPluginContext
│   ├── PluginLoggerAdapter.cs           ← IPluginLogger → ILogger
│   ├── PluginServicesAdapter.cs         ← IPluginServices → IServiceProvider
│   └── PluginEnvironmentAdapter.cs      ← IPluginEnvironment → host services
├── Lifecycle/
│   ├── ISdkCompatibilityValidator.cs
│   ├── SdkCompatibilityValidator.cs
│   ├── PluginActivator.cs
│   └── PluginLifecycleManager.cs
├── Hosting/
│   ├── PluginRuntimeManager.cs          ← NEW: orchestrates Loader+Activator+Lifecycle
│   └── PluginHost.cs                    ← MODIFIED: delegates to PluginRuntimeManager
├── Extensions/                          ← reserved for 14C
└── Health/
    └── PluginHealthCheck.cs             ← MODIFIED: uses new PluginStatus
```

---

## `PluginActivator`

Builds the per-plugin sub-container and instantiates `IPlugin`.

```
Pipeline:
1. Get manifest.EntryType from descriptor
2. Resolve type: assembly.GetType(entryType)
3. Verify type != null (else → Failed(EntryTypeVerification))
4. Verify typeof(IPlugin).IsAssignableFrom(type) (else → Failed(SdkCompatibility))
5. Verify type has public parameterless constructor (else → Failed(Activation))
6. Create plugin ServiceCollection
7. Register bridge adapters:
   - PluginLoggerAdapter    (wraps ILoggerFactory.CreateLogger(pluginId))
   - PluginConfigurationAdapter (appsettings section > plugin.config.json)
   - PluginEnvironmentAdapter (wraps IHostEnvironment + HostVersion + paths)
8. Build IServiceProvider (plugin-private; isolated from host container)
9. Create PluginServicesAdapter wrapping plugin provider
10. Construct PluginContext (immutable after creation; one instance per plugin lifetime)
11. Instantiate plugin: (IPlugin)Activator.CreateInstance(type)!
12. Store in PluginRuntime: Instance, PluginServices, Context, State = Loaded
```

**Rule:** Plugins must have a public parameterless constructor in 14B. Constructor injection into the plugin type itself is deferred to 14C.

**Rule:** `PluginContext` is created once in step 10 and never replaced or mutated. The same instance is passed to `InitializeAsync` and remains accessible via `PluginBase.Context` for the plugin lifetime.

---

## `PluginLifecycleManager`

Orchestrates all lifecycle phases with timeout enforcement and failure isolation.

### Startup ordering

Plugins are sorted **ascending** by `startupOrder` (then by `PluginId` for stable tie-breaking).

Shutdown is **descending** by `startupOrder` (reverse of startup).

### Timeout enforcement

Each phase uses a **linked `CancellationToken`**: whichever fires first — host shutdown or per-phase timeout.

```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
cts.CancelAfter(TimeSpan.FromSeconds(timeout));
await plugin.InitializeAsync(context, cts.Token);
```

Timeout fires → `OperationCanceledException` caught → state = `Failed`, `LastException` set, log `PluginHost1009`.

### Phase behavior

**`InitializeAllAsync`** (startup, sequential, ascending order):
- Skip plugins not in `Loaded` state.
- State → `Initializing`; call `InitializeAsync`; state → `Initialized` (success) or `Failed` (exception/timeout).
- `Failed` plugins: never call `StartAsync`. Continue to next plugin.
- Log `PluginHost1006` on success, `PluginHost1003` on failure.

**`StartAllAsync`** (startup, sequential, ascending order):
- Skip plugins not in `Initialized` state.
- State → `Starting`; call `StartAsync`; state → `Running` (success) or `Failed` (exception/timeout).
- `Failed` plugins at this stage: log, continue to next plugin.
- Log `PluginHost1007` on success.

**`StopAllAsync`** (shutdown, sequential, descending order):
- Skip plugins not in `Running` or `Initialized` state.
- State → `Stopping`; call `StopAsync`; state → `Stopped`.
- Exceptions logged but never rethrown. Continue to next plugin.
- Log `PluginHost1008`.

**`DisposeAllAsync`** (shutdown, sequential, descending order):
- All plugins regardless of state (except `Disposed`, `Disabled`).
- Call `DisposeAsync`; state → `Disposed`.
- Exceptions logged but never rethrown.
- Log `PluginHost1010`.

**Failure isolation:** One plugin failure never cascades. Every phase iterates all applicable plugins even if prior plugins in the same phase have failed.

---

## `PluginRuntimeManager`

Thin orchestrator. Keeps `PluginHost` minimal.

```
PluginHost.StartAsync:
  1. await runtimeManager.LoadAndActivateAsync(ct)      (Loader + Activator)
  2. await runtimeManager.InitializeAsync(ct)           (LifecycleManager)
  3. await runtimeManager.StartAsync(ct)                (LifecycleManager)
  4. registry.MarkInitialized()
  5. Log extended startup summary (PluginHost1005)

PluginHost.StopAsync:
  1. await runtimeManager.StopAsync(ct)                 (LifecycleManager)
  2. await runtimeManager.DisposeAsync(ct)              (LifecycleManager)
  3. Unload all AssemblyLoadContexts                    (existing)
```

### Extended startup summary (PluginHost1005)

```
Plugin host started.
Discovered={n} Loaded={n} Initialized={n} Running={n} Failed={n} Disabled={n}
LoadElapsed={ms}ms InitializeElapsed={ms}ms StartElapsed={ms}ms TotalElapsed={ms}ms
```

---

## Configuration Layering

`PluginConfigurationAdapter` merges two sources at construction time. Host appsettings wins.

```
Priority 1 (high): IConfiguration["Plugins:{pluginId}:*"]
Priority 2 (low):  {pluginDirectory}/plugin.config.json
```

**Policy for malformed `plugin.config.json`:** Log warning, use only priority-1 values. Never fail the plugin activation due to a bad config file — the host appsettings path still works.

**No runtime reload in 14B.** Config is resolved once at activation.

---

## `PluginHostOptions` Changes

```csharp
public sealed class PluginHostOptions
{
    public string PluginsPath             { get; set; } = "plugins";
    public string HostVersion             { get; set; } = "1.0.0";
    public int    DefaultTimeoutSeconds   { get; set; } = 30;      // fallback
    public int?   InitializeTimeoutSeconds{ get; set; }            // null → DefaultTimeout
    public int?   StartTimeoutSeconds     { get; set; }
    public int?   StopTimeoutSeconds      { get; set; }
    public int?   DisposeTimeoutSeconds   { get; set; }            // null → DefaultTimeout
    public string SupportedSdkMajorVersion{ get; set; } = "1";
    public string SupportedApiVersion     { get; set; } = "1";
    public int    MaxPluginCount          { get; set; } = 100;     // discovery stops after this
    public long   MaxManifestSizeBytes    { get; set; } = 65_536;  // 64 KB
    public long   MaxPluginConfigSizeBytes{ get; set; } = 1_048_576; // 1 MB
}
```

Per-phase timeout resolves as: `{Phase}TimeoutSeconds ?? DefaultTimeoutSeconds`.

---

## Plugin Folder Validation

Performed during discovery (stage 1 of the loading pipeline):

| Condition | Result |
|---|---|
| Symbolic link that resolves outside `PluginsPath` | Skip directory, log warning |
| Plugin path not normalized to canonical form | Normalize with `Path.GetFullPath` before processing |
| `plugin.json` missing | Skip directory (existing behavior) |
| `manifest.entryAssembly` DLL missing | `Failed(stage: AssemblyLoad)` |
| `resources/` folder missing | Allowed — non-fatal |
| Unknown subdirectory (not `lib/`, `resources/`, `logs/`) | Log warning, continue loading |
| Plugin count exceeds `MaxPluginCount` | Skip remaining, log warning with count |
| `plugin.json` exceeds `MaxManifestSizeBytes` | `Failed(stage: ManifestParse)` |
| `plugin.config.json` exceeds `MaxPluginConfigSizeBytes` | Non-fatal warning, treat as missing |

---

## Cancellation Semantics

Both host shutdown and per-phase timeout produce `OperationCanceledException`. The host distinguishes them by which token fired:

```
host cancellation token fired
    → StopAsync/DisposeAsync already in progress (normal shutdown path)
    → state remains Stopping / Disposing

per-phase timeout token fired
    → phase exceeded its timeout, host did not initiate shutdown
    → exception caught → state = Failed, LastException set
    → log PluginHost1009 (timeout) — never log as "host shutdown"
```

**Rule:** The `linkedCts` in `PluginLifecycleManager` wraps `CancellationTokenSource.CreateLinkedTokenSource(hostCt)` with `CancelAfter(timeout)`. After catching `OperationCanceledException`, check `hostCt.IsCancellationRequested` to distinguish timeout from shutdown: if only the linked source fired → timeout → `Failed`; if `hostCt` also fired → normal shutdown path.

---

## Logging Scopes

Every log line emitted inside a plugin lifecycle phase carries a structured scope with these standard keys:

```csharp
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["PluginId"]      = runtime.Descriptor.PluginId,
    ["PluginVersion"] = runtime.Descriptor.Version,
    ["Phase"]         = phaseName  // "Initialize" | "Start" | "Stop" | "Dispose"
}))
{
    await plugin.InitializeAsync(context, ct);
}
```

Structured logging sinks (Seq, Application Insights) automatically index these keys. Plugin-authored log calls via `IPluginLogger` use the same scope — `PluginLoggerAdapter.BeginScope` forwards to the underlying `ILogger`.

---

## Logging Event IDs (extensions)

| ID | Event |
|---|---|
| `PluginHost1001` | Directory discovered |
| `PluginHost1002` | Plugin loaded (assembly) |
| `PluginHost1003` | Plugin failed |
| `PluginHost1004` | Plugin disabled |
| `PluginHost1005` | Startup summary |
| `PluginHost1006` | Plugin initialized ← new 14B |
| `PluginHost1007` | Plugin started ← new 14B |
| `PluginHost1008` | Plugin stopped ← new 14B |
| `PluginHost1009` | Plugin lifecycle timeout ← new 14B |
| `PluginHost1010` | Plugin disposed ← new 14B |

---

## Health Check Changes

`PluginHealthCheck` maps `PluginStatus` to health:

| Plugin Status | Health contribution |
|---|---|
| `Running` | Healthy |
| `Initialized` | Healthy |
| `Loaded` | Healthy |
| `Stopped` | Healthy (host-initiated stop completed normally) |
| `Disabled` | Healthy (ignored) |
| `Failed` | Degraded (lists pluginId + error) |

**`Stopped` is always intentional in this state machine.** `Stopped` is only reachable when `StopAsync` completes without exception. If `StopAsync` throws or times out, the plugin transitions to `Failed`, not `Stopped`. Therefore no ambiguity: `Stopped` = host called stop, plugin cooperated cleanly = Healthy.

`Failed` is the only state that reflects an unexpected condition and triggers `Degraded`.

---

## REST API Impact

`GET /api/v1/plugins` now returns enriched status:

```json
{
  "pluginId": "msosync.test",
  "status": "Running",
  "loadDurationMs": 42,
  "initializeDurationMs": 18,
  "startDurationMs": 5,
  "loadedAt": "2026-07-15T...",
  "initializedAt": "2026-07-15T...",
  "startedAt": "2026-07-15T..."
}
```

`PluginDto` gains: `initializeDurationMs`, `startDurationMs`, `totalDurationMs`, `initializedAt`, `startedAt`.

---

## Frontend Changes

`PluginStatusBadge` — two new status mappings (icon + color, not color-only):

| Status | Variant | Icon |
|---|---|---|
| `Running` | `active` (green) | ✓ |
| `Initialized` | `warning` (yellow) | ⏳ |
| `Loaded` | `warning` (yellow) | ⏳ |
| `Stopped` | `neutral` (grey) | ■ |
| `Failed` | `error` (red) | ✕ |
| `Disabled` | `neutral` (grey) | ○ |

No new pages, no new routes.

---

## Testing Strategy

### `MSOSync.SdkTests`

- `PublicApiMatchesApproved()` — golden API test using `PublicApiAnalyzer` or equivalent; fails if public surface changes unexpectedly.
- `PluginBase_DefaultMethods_ReturnCompleted` — all default methods return `Task.CompletedTask` / `ValueTask.CompletedTask`.
- `PluginBase_InitializeAsync_CachesContext` — `Context` property set after `InitializeAsync`.
- `PluginCapability_FlagsCombinations` — bitwise OR/AND work correctly.

### `MSOSync.PluginTests` (unit, extended)

- `ISdkCompatibilityValidatorTests` — SDK 1.x accepted, 2.x rejected; API version mismatch rejected.
- `PluginActivatorTests` — missing type → Failed; type not IPlugin → Failed; activation exception → Failed; happy path → Instance not null.
- `PluginConfigurationTests` — appsettings wins over file; missing file is non-fatal; malformed file logs warning and uses appsettings.
- `PluginLifecycleManagerTests` (table-driven) — all 11 state transitions; timeout → Failed; exception → Failed; failed plugin skipped in Start; descending stop order; failure in one plugin does not block others.
- `PluginRuntimeManagerTests` — full startup sequence with mock loader + activator.

### `MSOSync.Plugin.IntegrationTests` (new project, real DLL)

Uses `MSOSync.TestPlugin` (implements `IPlugin`, references only `MSOSync.Sdk`).

| Test | Scenario |
|---|---|
| `FullLifecycle_ValidPlugin_ReachesRunning` | TestPlugin → state = Running |
| `InitializeAsync_Timeout_PluginFailed_OthersContinue` | SlowInitPlugin exceeds timeout → Failed; other plugins continue |
| `StartAsync_Throws_PluginFailed_OthersContinue` | ThrowingStartPlugin → Failed; other plugins reach Running |
| `StopAsync_Throws_Logged_OthersStopped` | ThrowingStopPlugin stops; log has error; others stop |
| `PluginConfig_AppsettingsWinsOverFile` | Both sources set same key; appsettings value returned |
| `PluginConfig_MalformedFile_NonFatal` | Bad plugin.config.json → plugin activates; appsettings config used |
| `StartupOrder_Ascending` | Three plugins with orders 200, 100, 300 → initialized in order 100→200→300 |
| `DuplicatePluginId_FirstWins_SecondFails` | Two dirs same id → first Running, second Failed |
| `SdkVersion_Mismatch_PluginFailed` | Plugin declares `sdkVersion: "2.0"` → Failed(SdkCompatibility) |
| `Health_FailedPlugin_ReturnsDegraded` | Plugin Failed → /health/ready returns Degraded |

### `MSOSync.TestPlugin` changes

Update to implement `IPlugin` (reference `MSOSync.Sdk` only). Record lifecycle calls for test assertions:

```csharp
public sealed class TestPlugin : PluginBase
{
    public static bool InitializeCalled { get; private set; }
    public static bool StartCalled      { get; private set; }
    public static bool StopCalled       { get; private set; }
    public static bool DisposeCalled    { get; private set; }

    public override Task InitializeAsync(IPluginContext ctx, CancellationToken ct)
        { InitializeCalled = true; return base.InitializeAsync(ctx, ct); }
    public override Task StartAsync(CancellationToken ct)
        { StartCalled = true;      return base.StartAsync(ct); }
    public override Task StopAsync(CancellationToken ct)
        { StopCalled = true;       return base.StopAsync(ct); }
    public override ValueTask DisposeAsync()
        { DisposeCalled = true;    return base.DisposeAsync(); }
}
```

Add `plugin.json` fields: `sdkVersion: "1.0"`, `apiVersion: "1"`, `startupOrder: 1000`.

---

## Implementation Order (9 tasks)

| Task | Deliverable | Notes |
|---|---|---|
| 1 | `MSOSync.Sdk` — all interfaces, enums, `PluginBase`, `PluginMetadata` | Builds independently; zero NuGet deps |
| 2 | `MSOSync.SdkTests` — golden API test + PluginBase tests | Validates SDK purity before touching host |
| 3 | Update `MSOSync.TestPlugin`: implement `IPlugin`, update `plugin.json` | Validates SDK isolation (references Sdk only) |
| 4 | Bridge adapters: `PluginLoggerAdapter`, `PluginEnvironmentAdapter`, `PluginServicesAdapter` | Adapts host services to SDK interfaces |
| 5 | `PluginConfigurationAdapter` + `PluginConfigurationFile` + `PluginConfigurationTests` | Layered config; malformed-file test |
| 6 | `ISdkCompatibilityValidator` + `SdkCompatibilityValidator`; extend `PluginManifestValidator` (sdkVersion, apiVersion, startupOrder); `PluginActivator` + `PluginActivatorTests` | IPlugin check, parameterless ctor check |
| 7 | `PluginLifecycleManager` + table-driven lifecycle tests | All 11 transitions, timeouts, failure isolation |
| 8 | `PluginRuntimeManager`; extend `PluginHost` (delegate to PluginRuntimeManager); extend `PluginRuntime` (timestamps, durations, Context, LastException); extend `PluginHostOptions`; update `PluginStatus` enum; update `PluginHealthCheck`; update `PluginController` DTOs; extend startup summary log | Full wiring + API surface update |
| 9 | `MSOSync.Plugin.IntegrationTests` (10 tests); update frontend `PluginStatusBadge` | Completion gate |

---

## Operational Hardening (covered in loading pipeline)

- **Duplicate `startupOrder`**: allowed; tie-broken by `PluginId` ascending (deterministic ordering).
- **Duplicate `capabilities`/`permissions` entries in manifest**: rejected → `Failed(stage: ManifestValidation)` (already validated; confirmed required).
- **Symbolic links**: any plugin path that resolves outside `PluginsPath` → skip + warning.
- **Path normalization**: all plugin directory paths normalized with `Path.GetFullPath` before use.
- **Max plugin count** (`MaxPluginCount=100`): discovery stops once limit reached; remaining dirs skipped with warning.
- **Max manifest size** (`MaxManifestSizeBytes=65536`): read manifest file size first; exceeded → `Failed(ManifestParse)`.
- **Max config size** (`MaxPluginConfigSizeBytes=1048576`): `plugin.config.json` exceeds limit → non-fatal warning; only appsettings values used.

## Design Deferred to Future Epics

- **Strong `PluginId` type** (`readonly record struct PluginId(string Value)`) — prevents host code from accidentally mixing bare strings. Not required for 14B; worth introducing before public SDK release.
- Constructor injection for plugin types (14C).
- Hot reload / dynamic plugin activation without restart.
- Plugin-to-plugin communication.
- Extension point invocation — collectors, operations, routing, transport (14C).
- First-party plugins (14D).
- DB-stored plugin configuration (14C or 14D).
- Plugin marketplace / upload UI.

## What 14B Does NOT Include

*(See "Design Deferred to Future Epics" above for the full list.)*
