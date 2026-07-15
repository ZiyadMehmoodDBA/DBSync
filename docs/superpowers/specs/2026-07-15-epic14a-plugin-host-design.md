# Epic 14A: Plugin Host — Design Specification

**Date:** 2026-07-15
**Status:** Approved
**Scope:** Plugin Host infrastructure only. No SDK, no plugin execution, no extension points, no marketplace.

---

## Goal

Build a production-ready plugin host for MSOSync that discovers, validates, loads, and tracks plugins from a `plugins/` folder at startup. Plugins are isolated via `AssemblyLoadContext`. A bad plugin never blocks host startup. The host exposes an admin API and diagnostics UI. Plugin execution and the extension SDK are deferred to Epic 14B.

---

## Architecture Overview

```
D:\MSOSync\
├── plugins\                          ← scanned at startup
│   └── msosync.sqlserver\
│       ├── plugin.json
│       ├── MSOSync.SqlCollector.dll
│       └── lib\                      ← plugin-private dependencies

src\
├── MSOSync.Plugin\                   ← new project
│   ├── Abstractions\
│   │   ├── IPluginHost.cs
│   │   ├── IPluginRegistry.cs
│   │   ├── IPluginLoader.cs
│   │   └── IPluginStore.cs
│   ├── Models\
│   │   ├── PluginManifest.cs
│   │   ├── PluginDescriptor.cs
│   │   ├── PluginRecord.cs
│   │   └── PluginLoadResult.cs
│   ├── Loading\
│   │   ├── PluginLoadContext.cs
│   │   ├── PluginLoader.cs
│   │   ├── PluginManifestValidator.cs
│   │   └── PluginDependencyResolver.cs
│   ├── Registry\
│   │   └── PluginRegistry.cs
│   ├── Hosting\
│   │   └── PluginHost.cs
│   └── Diagnostics\
│       └── PluginHealthCheck.cs
│
├── MSOSync.Persistence\
│   ├── Entities\SyncPlugin.cs
│   ├── Configurations\SyncPluginConfiguration.cs
│   ├── Migrations\M029_Plugins.cs
│   └── Stores\PluginStore.cs         ← implements IPluginStore
│
├── MSOSync.Api\
│   └── Controllers\PluginController.cs
│
└── MSOSync.Frontend\
    └── src\features\plugins\
        ├── types.ts
        ├── api.ts
        ├── hooks.ts
        ├── PluginStatusBadge.tsx
        ├── PluginSummaryCard.tsx
        ├── PluginsPage.tsx
        └── PluginsPage.test.tsx
```

**Dependency rule:** `MSOSync.Plugin` depends only on `MSOSync.Common`. It does NOT reference `MSOSync.Persistence` — the store abstraction (`IPluginStore`) is in `MSOSync.Plugin`; the EF implementation (`PluginStore`) is in `MSOSync.Persistence`. `MSOSync.App` wires them together.

---

## Plugin Folder Structure

```
plugins/
  {plugin-id}/
    plugin.json            ← required
    {EntryAssembly}.dll    ← required
    lib/                   ← optional: private dependencies
      *.dll
```

Each plugin lives in its own subdirectory under `plugins/`. The directory name is advisory — `plugin.json` is the authoritative source of the plugin ID.

---

## Plugin Manifest — `plugin.json`

```json
{
  "id": "msosync.sqlserver.collector",
  "name": "SQL Server Collector",
  "version": "1.0.0",
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "MSOSync.SqlCollector.dll",
  "entryType": "MSOSync.SqlCollector.Plugin",
  "author": "MSOSync",
  "description": "Collects SQL Server metrics.",
  "permissions": ["Collectors"],
  "dependencies": ["msosync.sqlserver.common"],
  "capabilities": []
}
```

### Field definitions

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Unique plugin identifier (reverse-DNS style) |
| `name` | string | yes | Human-readable display name |
| `version` | string | yes | Semantic version (major.minor.patch) |
| `minHostVersion` | string | yes | Minimum MSOSync host version (System.Version) |
| `maxHostVersion` | string | yes | Maximum MSOSync host version (System.Version) |
| `entryAssembly` | string | yes | DLL filename (no path, must be in plugin dir) |
| `entryType` | string | yes | Fully-qualified type name to verify on load |
| `author` | string | yes | Plugin author |
| `description` | string | yes | Short description |
| `permissions` | string[] | no | Declared capability requirements (for future enforcement) |
| `dependencies` | string[] | no | Required plugin IDs (must already be loaded) |
| `capabilities` | string[] | no | Reserved for 14B capability declarations |

---

## Loading Pipeline

Nine sequential stages. Failure at any stage produces `PluginLoadResult.Failed(stage, error)` and moves to the next plugin. The host never throws or aborts.

```
1. DISCOVER
   Enumerate plugins/ subdirectories. Collect directories containing plugin.json.
   Emit PluginHost1001 log per directory found.

2. PARSE
   Deserialize plugin.json into PluginManifest.
   Failure: malformed JSON → Failed(stage: Parse).

3. MANIFEST VALIDATION (PluginManifestValidator)
   • All required fields present
   • `id` unique across all discovered manifests (first wins, second → Failed)
   • `version` is valid semantic version (System.Version parseable)
   • `entryAssembly` filename contains no path separators (path traversal guard)
   • `entryAssembly` file exists in the plugin directory
   • `entryType` not null or whitespace
   • `permissions` has no duplicates
   • `dependencies` has no duplicates
   Failure: any check fails → Failed(stage: ManifestValidation).

4. FILTER
   Query IPluginStore for enabled state.
   If plugin record exists and Enabled = false → LoadResult.Disabled; skip remaining stages.

5. HOST COMPATIBILITY
   Compare System.Version(minHostVersion) ≤ hostVersion ≤ System.Version(maxHostVersion).
   Host version sourced from assembly version of MSOSync.App.
   Failure: out of range → Failed(stage: HostCompatibility).

6. DEPENDENCY RESOLUTION (PluginDependencyResolver)
   Plugins are processed in alphabetical order by directory name (one pass only).
   For each declared dependency plugin ID: verify it is already registered as Loaded in IPluginRegistry.
   Failure: dependency not loaded → Failed(stage: DependencyResolution).
   Note: plugins with dependencies must be in directories that sort alphabetically after their dependencies. Document this 14A limitation; dependency graphs are a 14B concern.

7. LOAD (PluginLoadContext)
   Create new PluginLoadContext(pluginDirectory, libDirectory).
   AssemblyLoadContext resolves assemblies from plugin dir first, then lib/, then falls back to host.
   Load entryAssembly via context.LoadFromAssemblyPath(path).
   Failure: assembly load exception → Failed(stage: AssemblyLoad).

8. VERIFY ENTRY TYPE
   Call assembly.GetType(entryType).
   Verify the type exists in the loaded assembly.
   DO NOT instantiate it. DO NOT call any methods. This is purely a metadata check.
   Failure: type not found → Failed(stage: EntryTypeVerification).

9. METADATA REGISTRATION
   Build PluginDescriptor.
   Call IPluginRegistry.Register(descriptor) → status = Loaded.
   Call IPluginStore.UpsertAsync(record) to persist current state.
   Emit PluginHost1002 log.
```

### Load result outcomes

| Outcome | Description |
|---|---|
| `Success` | All stages passed; plugin registered as Loaded |
| `Skipped` | Plugin directory found but no plugin.json |
| `Disabled` | Filtered out at stage 4 |
| `Failed` | Any stage 2–8 error; plugin registered as Failed |

---

## Models

### `PluginManifest`

Deserializes from `plugin.json`. Properties match the JSON fields exactly.

### `PluginRuntime` (internal runtime object)

Held by `IPluginRegistry`. Never exposed via API. Contains runtime internals:

```csharp
internal sealed record PluginRuntime
{
    public string PluginId              { get; init; }
    public PluginManifest Manifest      { get; init; }  // cached from parse, zero disk IO after startup
    public PluginStatus Status          { get; set; }
    public string? ErrorMessage         { get; set; }
    public string? FailureStage         { get; set; }
    public DateTime LoadedAt            { get; init; }
    public TimeSpan LoadDuration        { get; init; }
    public string PluginDirectory       { get; init; }
    public string HostCompatibility     { get; init; }  // "Compatible" | "Incompatible"
    public Assembly? Assembly           { get; init; }
    public AssemblyLoadContext? LoadContext { get; init; }
}
```

### `PluginDescriptor` (public DTO, returned from registry to API)

Lightweight — no Assembly or LoadContext references:

```csharp
public sealed record PluginDescriptor
{
    public string PluginId          { get; init; }
    public string Name              { get; init; }
    public string Version           { get; init; }
    public PluginStatus Status      { get; init; }
    public string? ErrorMessage     { get; init; }
    public string? FailureStage     { get; init; }
    public DateTime LoadedAt        { get; init; }
    public long LoadDurationMs      { get; init; }
    public string HostCompatibility { get; init; }
    public IReadOnlyList<string> Capabilities  { get; init; }
    public IReadOnlyList<string> Permissions   { get; init; }
    public IReadOnlyList<string> Dependencies  { get; init; }
    public PluginManifest Manifest  { get; init; }  // for GET /{id}/manifest endpoint
}
```

### `PluginStatus`

```csharp
public enum PluginStatus { Discovered, Validated, Loaded, Disabled, Failed }
```

### `PluginRecord` (persistence)

Maps to `sync_plugin`:

| Column | Type | Notes |
|---|---|---|
| `plugin_id` | nvarchar(200) PK | |
| `plugin_name` | nvarchar(200) | |
| `plugin_version` | nvarchar(50) | |
| `status` | nvarchar(20) | PluginStatus enum name |
| `enabled` | bit | default true |
| `installed_at` | datetime2 | first seen |
| `last_seen_at` | datetime2 | updated each startup |
| `last_error` | nvarchar(2000) | null on success |
| `manifest_hash` | nvarchar(64) | SHA-256 of plugin.json content |
| `host_version` | nvarchar(50) | host version at load time |

### `PluginLoadResult`

```csharp
public sealed record PluginLoadResult(
    string PluginId,
    PluginLoadOutcome Outcome,   // Success | Skipped | Disabled | Failed
    string? FailureStage,
    string? ErrorMessage);
```

---

## Abstractions

### `IPluginRegistry`

```csharp
public interface IPluginRegistry
{
    bool IsInitialized { get; }
    IReadOnlyList<PluginDescriptor> GetAll();
    PluginDescriptor? GetById(string pluginId);
    void Register(PluginDescriptor descriptor);
    void UpdateStatus(string pluginId, PluginStatus status, string? error = null);
    void MarkInitialized();
}
```

Singleton. `IsInitialized` is false until `PluginHost` calls `MarkInitialized()` after all plugins are processed. API returns 503 if registry is not yet initialized.

### `IPluginLoader`

```csharp
public interface IPluginLoader
{
    Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(string pluginsPath, CancellationToken ct);
}
```

### `IPluginStore`

```csharp
public interface IPluginStore
{
    Task<IReadOnlyList<PluginRecord>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PluginRecord record, CancellationToken ct);
    Task TouchAsync(string pluginId, CancellationToken ct);
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct);
}
```

`TouchAsync` updates `last_seen_at` and clears `last_error` for a successfully loaded plugin without rewriting all metadata.

---

## Database Schema — M029

New table `msosync.sync_plugin`. No foreign keys. No cascade. Standalone metadata store.

Migration adds one table; table count goes from 42 → 43. Update `PersistenceTests.SchemaCreated_All42TablesExist` → `SchemaCreated_All43TablesExist`.

---

## Hosted Service — `PluginHost`

Implements `IHostedService`. Runs once at `StartAsync`. Not a background loop.

```
StartAsync:
  stopwatch.Start()
  results = await loader.LoadAllAsync(pluginsPath, ct)
  registry.MarkInitialized()
  stopwatch.Stop()
  Log startup summary (PluginHost1005):
    Elapsed: {ms}ms
    Discovered: {n}
    Enabled: {n}
    Loaded: {n}
    Disabled: {n}
    Failed: {n}
  For each Failed result: Log PluginHost1003 with pluginId, stage, reason
```

`pluginsPath` resolved from configuration key `PluginHost:PluginsPath`, defaulting to `Path.Combine(AppContext.BaseDirectory, "plugins")`.

---

## Health Check — `PluginHealthCheck`

Registered with ASP.NET Core health system under tag `"plugins"`.

| Condition | Result |
|---|---|
| Registry not initialized | `Unhealthy` ("Plugin host not yet started") |
| All enabled plugins `Loaded` | `Healthy` |
| Any enabled plugin `Failed` | `Degraded` (lists failed plugin IDs and their errors) |
| All plugins disabled | `Healthy` |

Disabled plugins are excluded from health evaluation.

---

## Admin API — `PluginController`

Base route: `api/v1/plugins`. Authorization: `[Authorize(Policy = "AdminOnly")]`.

| Method | Route | Response | Description |
|---|---|---|---|
| GET | `/` | `PluginDto[]` | All plugins from registry |
| GET | `/summary` | `PluginSummaryDto` | Counts + startup duration |
| GET | `/{id}` | `PluginDto` | Single plugin detail |
| GET | `/{id}/manifest` | `PluginManifest` | Parsed manifest (no disk read at call time) |
| POST | `/{id}/enable` | `PluginActionResult` | Set enabled = true |
| POST | `/{id}/disable` | `PluginActionResult` | Set enabled = false |

### `PluginDto`

```
pluginId, name, version, status, enabled, loadDurationMs, loadedAt,
lastError, failureStage, hostCompatibility,
capabilities, permissions, dependencies
```

### `PluginSummaryDto`

```
total, loaded, failed, disabled, startupDurationMs, lastScanAt
```

### `PluginActionResult`

```json
{ "success": true, "restartRequired": true }
```

Enable/disable always returns `restartRequired: true` in 14A. Hot reload is not supported.

Returns 404 if plugin ID not found in registry. Returns 503 if registry not initialized.

---

## Logging Event IDs

| ID | Event |
|---|---|
| `PluginHost1001` | Plugin directory discovered |
| `PluginHost1002` | Plugin loaded successfully |
| `PluginHost1003` | Plugin failed to load (with stage + reason) |
| `PluginHost1004` | Plugin disabled (skipped) |
| `PluginHost1005` | Startup summary |

---

## Frontend — `/administration/plugins`

### Components

**`PluginSummaryCard`** — displayed on the Overview dashboard. Consumes `GET /api/v1/plugins/summary`. Shows Loaded / Failed / Disabled / Total / Startup duration. Admin-only visibility.

**`PluginsPage`** — full plugin list. Table columns:
- Name
- Version
- Status (coloured badge)
- Load Time
- Host Compatibility (Compatible / Incompatible — visible in table, not just on expand)
- Enable / Disable button

Expanded row shows: `lastError`, `failureStage`, `pluginDirectory`, manifest detail.

On enable/disable: `toast.info("Plugin {name} {enabled ? 'enabled' : 'disabled'}. Restart required to take effect.")`.

No upload, install, remove, or file management UI.

**`PluginStatusBadge`** — reusable. Green = Loaded, Red = Failed, Grey = Disabled, Yellow = Discovered/Validated.

### Route

`/administration/plugins` — added alongside existing admin routes. "Plugins" item added to Administration sidebar. ADMIN role only.

---

## Testing

### Unit tests — `tests/MSOSync.PluginTests/`

| Test class | Coverage |
|---|---|
| `PluginManifestValidatorTests` | Required fields, bad semver, path traversal in entryAssembly, duplicate id, missing assembly file |
| `PluginLoaderTests` | Bad manifest skipped, wrong host version → Failed, missing DLL → Failed, disabled → Skipped |
| `PluginRegistryTests` | IsInitialized gate (returns empty before MarkInitialized), Register/GetById/UpdateStatus |
| `PluginLoadContextTests` | Assembly loaded from plugin dir, lib/ probed for dependencies |
| `PluginDependencyResolverTests` | Missing dependency plugin ID → Failed(stage: DependencyResolution) |

### Integration tests — `tests/MSOSync.IntegrationTests/Plugins/`

| Test | Scenario |
|---|---|
| `PluginHost_ValidPlugin_RegistersAsLoaded` | Real test DLL in temp plugins/ → registry shows Loaded |
| `PluginHost_DisabledPlugin_IsSkipped` | `sync_plugin.enabled = false` → Skipped, not in Loaded registry |
| `PluginHost_FailedPlugin_DoesNotBlockStartup` | Bad manifest → host starts, other plugins load normally |
| `PluginHost_DuplicatePluginId_FirstWinsSecondFails` | Two folders same id → first Loaded, second Failed |
| `PluginHost_InvalidEntryType_RegistersAsFailed` | Manifest points to non-existent type → Failed(stage: EntryTypeVerification) |
| `GetPlugins_ReturnsRegistryContents` | GET /api/v1/plugins → 200 with correct list |
| `GetPluginSummary_ReturnsCorrectCounts` | GET /api/v1/plugins/summary → counts match |
| `DisablePlugin_PersistsAcrossRestart` | POST /disable → sync_plugin.enabled = false → survives restart |
| `GetPluginManifest_ReturnsParsedManifest` | GET /api/v1/plugins/{id}/manifest → 200 with manifest fields |
| `GetPlugins_BeforeInitialized_Returns503` | Request before PluginHost.StartAsync completes → 503 |

### Test plugin

A minimal compiled assembly `MSOSync.TestPlugin.dll` committed under `tests/MSOSync.IntegrationTests/TestAssets/plugins/test-plugin/`. Contains a single public class `MSOSync.TestPlugin.TestPlugin` with no logic. Accompanying `plugin.json` with `id: "msosync.test"`.

---

## Implementation Order

1. M029 migration + `SyncPlugin` entity + EF config + `PluginStore` + `IPluginStore`
2. `PluginManifest` + `PluginManifestValidator` (unit tests first)
3. `PluginLoadContext` + `PluginLoader` + `PluginDependencyResolver` (unit tests first)
4. `PluginDescriptor` + `PluginRegistry` + `PluginLoadResult` (unit tests)
5. `PluginHost` (IHostedService) + startup summary logging
6. `PluginHealthCheck` + health registration
7. `PluginController` + DI wiring in `MSOSync.App`
8. Frontend: types → api → hooks → PluginStatusBadge → PluginSummaryCard → PluginsPage → route
9. Integration tests

---

## Out of Scope (Deferred)

| Feature | Epic |
|---|---|
| `IPlugin` interface + lifecycle methods | 14B |
| `MSOSync.SDK` NuGet package | 14B |
| DI integration for plugins | 14B |
| Plugin activation (`Activator.CreateInstance`) | 14B |
| SQL Server Collector plugin | 14C |
| Hot reload / unload without restart | 14C+ |
| Plugin signing | Enterprise |
| Remote repository / marketplace | Enterprise |
| Sandboxing | Enterprise |
