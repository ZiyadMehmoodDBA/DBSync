# Phase 2C.5 — SDK Samples + Developer Portal

**Date:** 2026-07-23
**Status:** Approved
**Phase:** 2C — SDK & Ecosystem
**Sequence:** Executes after 2C.1 (Plugin Packaging Format) and 2C.4 (CLI Tooling) are complete.

---

## Goal

Convert MSOSync from a platform with an SDK into a platform developers can learn from, build on, and ship against. Deliver four official code samples covering the full SDK surface, two `dotnet new` project templates for scaffolding, and a static Markdown developer portal that serves as the canonical reference for plugin authors.

**Completion criteria (all must be true before 2C.5 is done):**

1. All four samples compile with `dotnet build` against `MSOSync.Sdk` (project reference in dev; NuGet reference in CI).
2. No sample references `MSOSync.Api`, `MSOSync.Metadata`, `MSOSync.Plugin`, or `MSOSync.Persistence`.
3. `dotnet new install MSOSync.Templates` completes without error.
4. Both templates scaffold a directory that compiles with zero errors and zero warnings.
5. All eight portal Markdown files exist under `docs/developer-portal/` with no broken internal links.
6. A CI build step (`samples/build-check.ps1`) builds all four samples in sequence and exits non-zero on any failure.

---

## Dependencies on Prior 2C Phases

### From 2C.1 — Plugin Packaging Format

- The `.msopkg` archive layout (manifest, DLL, `lib/`, `plugin.config.json`, signature block) must be finalized before the Packaging Guide (`packaging.md`) can be written with authoritative file paths.
- The canonical field set for `plugin.json` (including `manifestVersion`, `sdkVersion`, `apiVersion`, `startupOrder`) must be frozen — samples reference these fields in their own `plugin.json` files.
- The `manifest.json` vs `plugin.json` naming convention must be resolved. This spec uses `plugin.json` (the 14B convention). If 2C.1 renames the file, update all four sample `plugin.json` files and `packaging.md` before shipping.

### From 2C.4 — CLI Tooling

- The `msosync plugin pack` command syntax must be finalized before `packaging.md` documents the packaging workflow.
- The `msosync plugin publish` command must exist before `publishing.md` can be written with real examples.
- If CLI commands are not ready when 2C.5 begins, write `packaging.md` and `publishing.md` with explicit `[CLI: pending 2C.4]` markers rather than omitting the files. Do not leave these two portal pages as stubs — write the full content flow and mark only the specific CLI invocations as pending.

---

## Part A — Official SDK Samples

**Location:** `samples/` at repository root (sibling to `src/`, `tests/`, `docs/`).

**Isolation rule:** Samples live outside `MSOSync.sln`. Each sample has its own `.sln` or compiles standalone via its `.csproj`. The `MSOSync.sln` solution file is not modified. This prevents sample compilation errors from blocking the main build.

**SDK reference convention:**

```xml
<!-- Development (when building from repo) -->
<ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />

<!-- Published (when installed as NuGet package) -->
<PackageReference Include="MSOSync.Sdk" Version="1.0.0" />
```

Each sample `.csproj` uses a conditional reference controlled by an `$(MSOSyncSdkLocal)` MSBuild property:

```xml
<ItemGroup Condition="'$(MSOSyncSdkLocal)' == 'true'">
  <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(MSOSyncSdkLocal)' != 'true'">
  <PackageReference Include="MSOSync.Sdk" Version="1.0.0" />
</ItemGroup>
```

The CI build step sets `MSOSyncSdkLocal=true`. Published samples (NuGet gallery, developer portal downloads) ship without this property set — they consume the NuGet package.

**Target framework and language version:** All samples target `net9.0`, C# 13, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`. `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is set. These match the host project settings and ensure samples stay warning-clean.

---

### Sample 1 — `HelloWorldPlugin`

**Directory:** `samples/HelloWorldPlugin/`

**Purpose:** The minimal complete plugin. Demonstrates the absolute minimum a developer must implement to have a working plugin accepted by the MSOSync plugin host. Teaches the `IPlugin` contract, `PluginBase` convenience class, plugin manifest fields, and the lifecycle sequence (`InitializeAsync` → `StartAsync` → `StopAsync` → `DisposeAsync`).

**What it demonstrates:**

- Extending `PluginBase` instead of implementing `IPlugin` directly (explains why: default no-op implementations, `Context` caching).
- Overriding only `StartAsync` — proof that the other lifecycle methods are optional.
- Using `IPluginContext.Logger` to emit structured log lines via `IPluginLogger.LogInformation`.
- Reading `IPluginContext.Metadata` to log the plugin's own `PluginId`, `Name`, and `Version` at startup.
- Reading `IPluginContext.Environment.IsDevelopment` to branch diagnostic output.
- Correct `plugin.json` with all required fields (`id`, `name`, `version`, `sdkVersion`, `apiVersion`, `minHostVersion`, `maxHostVersion`, `entryAssembly`, `entryType`, `author`, `description`).
- `PluginPermission.None` and empty `capabilities` — the neutral baseline.

**Files:**

```
samples/HelloWorldPlugin/
├── HelloWorldPlugin.csproj
├── HelloWorldPlugin.cs          ← the plugin class
├── plugin.json                  ← manifest for the host
├── plugin.config.json           ← empty object {}; shows the file is expected
└── README.md
```

**`HelloWorldPlugin.cs` — implementation intent (not final code, but authoritative behavior):**

```csharp
using MSOSync.Sdk.Hosting;
using MSOSync.Sdk.Abstractions;

namespace HelloWorldPlugin;

public sealed class HelloWorldPlugin : PluginBase
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("HelloWorldPlugin.Start");

        Context.Logger.LogInformation(
            "Hello World from {PluginId} v{Version} (host: {HostVersion}, env: {Env})",
            Context.Metadata.PluginId,
            Context.Metadata.Version,
            Context.Environment.HostVersion,
            Context.Environment.EnvironmentName);

        if (Context.Environment.IsDevelopment)
        {
            Context.Logger.LogDebug(
                "Plugin directory: {PluginDir}, Data directory: {DataDir}",
                Context.Environment.PluginDirectory,
                Context.Environment.DataDirectory);
        }

        return Task.CompletedTask;
    }
}
```

**`plugin.json` — canonical manifest:**

```json
{
  "manifestVersion": 1,
  "id": "samples.hello-world",
  "name": "Hello World Plugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "HelloWorldPlugin.dll",
  "entryType": "HelloWorldPlugin.HelloWorldPlugin",
  "author": "MSOSync",
  "description": "Minimal plugin sample — demonstrates IPlugin lifecycle and IPluginLogger.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

**`plugin.config.json`:**

```json
{}
```

**README scope:** Install steps, what each file does, the lifecycle sequence diagram (text-based, ASCII), what to look for in host logs when this plugin loads.

---

### Sample 2 — `DataCollectorPlugin`

**Directory:** `samples/DataCollectorPlugin/`

**Purpose:** Demonstrates a plugin that performs real work on a timer — polling a SQL Server table, accumulating metrics, and exposing them via a registered service through `IPluginServices`. Teaches `PluginCapability.Collector`, periodic work patterns using `System.Threading.Timer`, safe async patterns inside a plugin, and how a plugin registers and exposes a service that other host components (or future extension points) can consume.

**What it demonstrates:**

- Declaring `PluginCapability.Collector` and `PluginPermission.Collectors` in the manifest and understanding their meaning.
- Resolving a connection string from `IPluginConfiguration` using `GetValue<string>("ConnectionString")` with a fallback default.
- Using `IPluginConfiguration.GetSection("Polling")` to read a structured configuration sub-section (`IntervalSeconds`, `TableName`).
- Starting a background `System.Threading.Timer` in `StartAsync` and disposing it cleanly in `DisposeAsync`.
- Logging at appropriate levels: `LogDebug` for per-cycle details, `LogWarning` for elevated row counts, `LogError` for SQL exceptions.
- Pattern for accumulating metrics in a `ConcurrentQueue<MetricSample>` thread-safely.
- The plugin does NOT use `IPluginServices.GetRequiredService` from the host — it demonstrates the other direction: the plugin itself being a service that could be queried. The `IPluginServices` interface is shown from both directions: accessing host-provided services (via `IPluginContext.Services`) and the concept that plugins can expose services to the host through future extension points (documented in the README, not wired in this sample because 14C extension points are not yet available).

**Files:**

```
samples/DataCollectorPlugin/
├── DataCollectorPlugin.csproj
├── DataCollectorPlugin.cs       ← timer loop, SQL polling
├── MetricSample.cs              ← record: TableName, RowCount, CollectedAt
├── plugin.json
├── plugin.config.json           ← ConnectionString, Polling section
└── README.md
```

**`plugin.config.json` — sample configuration:**

```json
{
  "ConnectionString": "Server=localhost;Database=MyDb;Trusted_Connection=True;",
  "Polling": {
    "IntervalSeconds": 30,
    "TableName": "dbo.SyncEvents"
  }
}
```

**`DataCollectorPlugin.csproj` extra dependency:** `Microsoft.Data.SqlClient` (NuGet). This is the only sample that adds a NuGet dependency beyond the SDK — intentional, to show that plugins may carry private dependencies in their `lib/` directory.

**Manifest capabilities and permissions:**

```json
{
  "capabilities": ["Collector"],
  "permissions": ["Collectors"]
}
```

**README scope:** Configuration reference (all keys, types, defaults), how to point at a real SQL Server, how to read the collected metrics in host logs, the `lib/` folder convention for private NuGet dependencies, thread-safety note on `ConcurrentQueue`.

---

### Sample 3 — `WebhookPlugin`

**Directory:** `samples/WebhookPlugin/`

**Purpose:** Demonstrates a plugin that integrates with an external system by listening for sync lifecycle signals and posting them to an HTTP webhook endpoint. Teaches `PluginCapability.Transport`, using `IPluginServices` to resolve a host-provided `IHttpClientFactory` (if available) with graceful fallback to a self-created `HttpClient`, and async outbound HTTP patterns.

**What it demonstrates:**

- `IPluginServices.GetService<IHttpClientFactory>()` — nullable return, fallback pattern when the service is not registered.
- Using `IPluginConfiguration` to read `WebhookUrl`, `TimeoutSeconds`, and `RetryCount`.
- Implementing `StartAsync` and `StopAsync` cleanly: posting a "plugin started" and "plugin stopped" webhook notification.
- Async HTTP with cancellation token forwarding from `StopAsync`.
- `LogWarning` on non-2xx HTTP responses without throwing — plugin never fails the host over a webhook delivery failure.
- `DisposeAsync` disposing the `HttpClient` when the plugin owns it.
- `PluginCapability.Transport` and `PluginPermission.Transport` in the manifest.

**Files:**

```
samples/WebhookPlugin/
├── WebhookPlugin.csproj
├── WebhookPlugin.cs             ← HTTP dispatch logic
├── WebhookPayload.cs            ← serializable payload record
├── plugin.json
├── plugin.config.json           ← WebhookUrl, TimeoutSeconds, RetryCount
└── README.md
```

**`plugin.config.json`:**

```json
{
  "WebhookUrl": "https://hooks.example.com/msosync",
  "TimeoutSeconds": 10,
  "RetryCount": 3
}
```

**Manifest capabilities and permissions:**

```json
{
  "capabilities": ["Transport"],
  "permissions": ["Transport"]
}
```

**README scope:** How to point at a real webhook receiver (Slack incoming webhook, Azure Logic App, etc.), retry behavior, how to verify delivery in host logs, why `GetService<T>` (not `GetRequiredService<T>`) is used here.

---

### Sample 4 — `ConfigDrivenPlugin`

**Directory:** `samples/ConfigDrivenPlugin/`

**Purpose:** The authoritative example of `IPluginConfiguration`. Demonstrates typed configuration binding, section navigation, defaults, existence checks, and the hot-reload pattern (manual polling, because the SDK does not provide a change-notification callback in SDK 1.0 — this sample shows the workaround and documents why it is necessary).

**What it demonstrates:**

- `IPluginConfiguration.GetValue<T>(key)` vs `GetValue<T>(key, defaultValue)` — when each form should be used.
- `IPluginConfiguration.GetSection("Section")` for nested config objects.
- `IPluginConfiguration.Exists(key)` for optional feature flags.
- `IPluginConfiguration.Keys` to enumerate all resolved keys at startup (useful for debugging misconfiguration).
- Binding to a typed settings record (`PluginSettings`) by iterating `Keys` and constructing the record manually — there is no `Bind<T>` method on `IPluginConfiguration`; this sample shows the correct pattern.
- Hot-reload via a `System.Threading.Timer` that re-reads configuration values periodically and compares them to cached values — logs when a value changes. Clearly documented as a workaround: SDK 1.0 does not push change notifications; SDK 2.0 will add `IPluginConfigurationMonitor<T>`.
- `IPluginEnvironment.IsDevelopment` gate: in development, log all resolved configuration keys at startup; in production, log only the count.

**Files:**

```
samples/ConfigDrivenPlugin/
├── ConfigDrivenPlugin.csproj
├── ConfigDrivenPlugin.cs        ← config read, hot-reload timer
├── PluginSettings.cs            ← typed settings record
├── plugin.json
├── plugin.config.json           ← rich multi-section example
└── README.md
```

**`plugin.config.json` — rich example:**

```json
{
  "Feature": {
    "EnableDetailedLogging": true,
    "MaxBatchSize": 500
  },
  "Retry": {
    "MaxAttempts": 3,
    "DelayMs": 1000
  },
  "Thresholds": {
    "WarnAtQueueDepth": 1000,
    "ErrorAtQueueDepth": 5000
  }
}
```

**`PluginSettings.cs` — typed binding pattern:**

```csharp
internal sealed record PluginSettings(
    bool   EnableDetailedLogging,
    int    MaxBatchSize,
    int    RetryMaxAttempts,
    int    RetryDelayMs,
    int    WarnAtQueueDepth,
    int    ErrorAtQueueDepth);
```

**Manifest capabilities and permissions:**

```json
{
  "capabilities": [],
  "permissions": []
}
```

This sample declares no capabilities — it is purely a configuration demonstration. No operational function.

**README scope:** Full field-by-field configuration reference, `GetSection` navigation examples, the hot-reload workaround and its limitations, the appsettings override pattern (how the host's `appsettings.json` `Plugins:samples.config-driven:*` section wins over `plugin.config.json`), the SDK 2.0 roadmap note for `IPluginConfigurationMonitor<T>`.

---

### Sample Directory Layout (Complete)

```
samples/
├── HelloWorldPlugin/
│   ├── HelloWorldPlugin.csproj
│   ├── HelloWorldPlugin.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
├── DataCollectorPlugin/
│   ├── DataCollectorPlugin.csproj
│   ├── DataCollectorPlugin.cs
│   ├── MetricSample.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
├── WebhookPlugin/
│   ├── WebhookPlugin.csproj
│   ├── WebhookPlugin.cs
│   ├── WebhookPayload.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
├── ConfigDrivenPlugin/
│   ├── ConfigDrivenPlugin.csproj
│   ├── ConfigDrivenPlugin.cs
│   ├── PluginSettings.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
└── build-check.ps1              ← CI build validation script
```

### `build-check.ps1` — CI Build Validation

Builds all four samples in sequence with `MSOSyncSdkLocal=true`. Exits with code 1 on any failure.

```powershell
$ErrorActionPreference = 'Stop'
$samples = @(
    'HelloWorldPlugin',
    'DataCollectorPlugin',
    'WebhookPlugin',
    'ConfigDrivenPlugin'
)
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$failed = @()

foreach ($sample in $samples) {
    $proj = Join-Path $root "$sample\$sample.csproj"
    Write-Host "Building $sample..."
    dotnet build $proj /p:MSOSyncSdkLocal=true --no-incremental -warnaserror
    if ($LASTEXITCODE -ne 0) { $failed += $sample }
}

if ($failed.Count -gt 0) {
    Write-Error "Failed: $($failed -join ', ')"
    exit 1
}
Write-Host "All samples built successfully."
```

---

## Part B — Project Templates

**Location:** `src/MSOSync.Templates/`

**Purpose:** Allow developers to scaffold a new plugin with a single `dotnet new` command, eliminating the friction of copying sample files and renaming types.

---

### Project Structure

```
src/MSOSync.Templates/
├── MSOSync.Templates.csproj
├── content/
│   ├── msosync-plugin/                   ← basic template
│   │   ├── .template.config/
│   │   │   └── template.json
│   │   ├── MyPlugin.cs
│   │   ├── MyPlugin.csproj
│   │   ├── plugin.json
│   │   └── plugin.config.json
│   └── msosync-plugin-advanced/          ← advanced template
│       ├── .template.config/
│       │   └── template.json
│       ├── MyPlugin.cs
│       ├── MyPluginSettings.cs
│       ├── MyPlugin.csproj
│       ├── plugin.json
│       └── plugin.config.json
└── README.md
```

### `MSOSync.Templates.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageType>Template</PackageType>
    <PackageVersion>1.0.0</PackageVersion>
    <PackageId>MSOSync.Templates</PackageId>
    <Title>MSOSync Plugin Templates</Title>
    <Authors>MSOSync</Authors>
    <Description>dotnet new templates for building MSOSync plugins.</Description>
    <TargetFramework>net9.0</TargetFramework>
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
    <NoDefaultExcludes>true</NoDefaultExcludes>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="content/**/*" PackagePath="content" />
  </ItemGroup>
</Project>
```

---

### Template 1 — `msosync-plugin` (Basic)

**Usage:**

```
dotnet new msosync-plugin --name MyAwesomePlugin --output ./MyAwesomePlugin
```

**What it scaffolds:** A single-file plugin that extends `PluginBase`, overrides `StartAsync`, and logs one `LogInformation` line. Mirrors `HelloWorldPlugin` with the developer's chosen name substituted throughout.

**`.template.config/template.json`:**

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "MSOSync",
  "classifications": ["MSOSync", "Plugin"],
  "identity": "MSOSync.Plugin.Basic",
  "name": "MSOSync Plugin",
  "shortName": "msosync-plugin",
  "tags": {
    "language": "C#",
    "type": "project"
  },
  "sourceName": "MyPlugin",
  "symbols": {
    "pluginId": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "my.plugin",
      "description": "Reverse-DNS plugin identifier (e.g. acme.my-plugin)"
    },
    "author": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "My Organization",
      "description": "Plugin author name"
    },
    "description": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "My MSOSync plugin.",
      "description": "Short plugin description"
    }
  }
}
```

**Substitutions applied by the template engine:**

| Template token | Replaced with |
|---|---|
| `MyPlugin` | `--name` value (e.g. `AwesomeCollector`) |
| `my.plugin` | `--pluginId` value |
| `My Organization` | `--author` value |
| `My MSOSync plugin.` | `--description` value |

**Content of `MyPlugin.cs` in the template:**

```csharp
using MSOSync.Sdk.Hosting;

namespace MyPlugin;

public sealed class MyPlugin : PluginBase
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.LogInformation(
            "{PluginId} started (host: {HostVersion})",
            Context.Metadata.PluginId,
            Context.Environment.HostVersion);

        return Task.CompletedTask;
    }
}
```

**Content of `plugin.json` in the template:**

```json
{
  "manifestVersion": 1,
  "id": "my.plugin",
  "name": "MyPlugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "MyPlugin.dll",
  "entryType": "MyPlugin.MyPlugin",
  "author": "My Organization",
  "description": "My MSOSync plugin.",
  "permissions": [],
  "dependencies": [],
  "capabilities": []
}
```

**Content of `plugin.config.json` in the template:**

```json
{}
```

---

### Template 2 — `msosync-plugin-advanced` (Config + Services)

**Usage:**

```
dotnet new msosync-plugin-advanced --name MyCollector --pluginId acme.my-collector --capability Collector
```

**What it scaffolds:** A plugin with a typed `MyPluginSettings` record, `IPluginConfiguration.GetSection` usage in `InitializeAsync`, a `System.Threading.Timer` started in `StartAsync` and disposed in `DisposeAsync`, and `IPluginServices.GetService<T>` with a null-safe fallback. Mirrors the structure of `DataCollectorPlugin`.

**Additional template parameter:**

```json
"capability": {
  "type": "parameter",
  "datatype": "choice",
  "choices": [
    { "choice": "None",      "description": "No capability declared" },
    { "choice": "Collector", "description": "Data collector plugin" },
    { "choice": "Transport", "description": "Transport/webhook plugin" },
    { "choice": "Operation", "description": "Operations plugin" }
  ],
  "defaultValue": "None",
  "description": "Primary plugin capability"
}
```

The `plugin.json` template file uses conditional blocks that the template engine evaluates to set `capabilities` and `permissions` based on the `--capability` parameter.

**Files scaffolded:**

- `MyPlugin.csproj` — with SDK conditional reference block.
- `MyPlugin.cs` — `InitializeAsync` loads settings, `StartAsync` starts timer, `StopAsync` stops timer cleanly, `DisposeAsync` disposes timer.
- `MyPluginSettings.cs` — typed settings record with fields matching `plugin.config.json`.
- `plugin.json` — manifest with capability/permission populated from template parameter.
- `plugin.config.json` — sample configuration matching the settings record fields.

---

### Template Installation and Validation

**Install from local pack (development):**

```
dotnet pack src/MSOSync.Templates/MSOSync.Templates.csproj -o ./artifacts
dotnet new install ./artifacts/MSOSync.Templates.1.0.0.nupkg
```

**Install from NuGet (published):**

```
dotnet new install MSOSync.Templates
```

**Verify templates are registered:**

```
dotnet new list --tag MSOSync
```

Expected output: two rows — `MSOSync Plugin` (`msosync-plugin`) and `MSOSync Plugin (Advanced)` (`msosync-plugin-advanced`).

**Validation rule:** Both templates must pass `dotnet new msosync-plugin --name TestOutput --dry-run` without error before the package is published. The CI build step for templates runs `dotnet pack` and `dotnet new install` + scaffold + `dotnet build` on both templates in a temp directory.

---

## Part C — Developer Portal

**Location:** `docs/developer-portal/`

**Format:** Markdown only. No web server, no static site generator, no build step. Files must render correctly on GitHub and in IDEs. All code blocks use fenced syntax with language identifiers. All cross-references use relative Markdown links (`[See lifecycle](plugin-lifecycle.md)`).

**Target audience:** A C# developer who has never used MSOSync, knows .NET and ASP.NET Core, and wants to build a plugin in under an hour.

---

### Portal File Index

```
docs/developer-portal/
├── getting-started.md
├── plugin-lifecycle.md
├── configuration.md
├── services.md
├── permissions.md
├── packaging.md
├── publishing.md
└── api-reference.md
```

---

### `getting-started.md` — Install SDK, Create First Plugin (5-minute guide)

**Sections:**

1. **Prerequisites** — .NET 9 SDK, MSOSync host installed and running, `dotnet new` available.
2. **Install the template** — `dotnet new install MSOSync.Templates`.
3. **Scaffold your first plugin** — `dotnet new msosync-plugin --name MyFirstPlugin`. Show the generated directory tree.
4. **Build and verify** — `dotnet build MyFirstPlugin.csproj`. Expected output: zero errors.
5. **Drop it into the host** — Copy the build output to `{host}/plugins/my-first-plugin/`. Show the expected directory layout.
6. **Restart and verify** — Restart MSOSync. Show the log line the host emits on successful plugin load (`PluginHost1002`) and the plugin's own log line from `StartAsync`.
7. **Next steps** — Links to `plugin-lifecycle.md`, `configuration.md`, `services.md`.

**Constraints:** Under 400 lines. Every command is in a fenced `bash` or `powershell` code block with the correct language tag. No prose that duplicates the lifecycle reference.

---

### `plugin-lifecycle.md` — Initialize / Start / Stop Contract

**Sections:**

1. **Overview** — The four lifecycle phases and their sequence. ASCII state diagram:

```
[Loaded] → [Initializing] → [Initialized] → [Starting] → [Running]
                                                                |
                                              [Stopping] ←─────┘
                                                  |
                                             [Stopped] → [Disposing] → [Disposed]
```

2. **`InitializeAsync`** — When it is called, what it guarantees, what `IPluginContext` carries at this point, when to throw vs. log-and-continue. Rule: expensive resource acquisition (DB connections, HTTP clients) belongs in `InitializeAsync`, not in the constructor.

3. **`StartAsync`** — When it is called (after all plugins have completed `InitializeAsync`), correct usage for starting timers and background threads. Warning: do not block `StartAsync` with infinite loops — use a background task.

4. **`StopAsync`** — When it is called (host shutdown, descending startup order), correct cancellation forwarding, maximum stop duration (governed by `PluginHostOptions.StopTimeoutSeconds`).

5. **`DisposeAsync`** — Always called regardless of plugin state. Dispose all managed resources here. `IAsyncDisposable` contract.

6. **`PluginBase` shortcut** — Explains that overriding only the methods the plugin needs is correct; unoverridden methods return `Task.CompletedTask`.

7. **Failure behavior** — What happens when `InitializeAsync` throws: plugin transitions to `Failed`, `StartAsync` is never called, other plugins are unaffected.

8. **Timeout behavior** — Host enforces per-phase timeouts. Plugins should respect the passed `CancellationToken`. `OperationCanceledException` on timeout → plugin transitions to `Failed`.

9. **Do not** list (antipatterns) — Block the constructor with I/O, store `CancellationToken` as a field, mutate `IPluginContext` after `InitializeAsync`, call `Environment.Exit`.

---

### `configuration.md` — IPluginConfiguration Guide

**Sections:**

1. **Two-source model** — `plugin.config.json` (low priority) vs. host `appsettings.json` `Plugins:{pluginId}:*` section (high priority). When each is appropriate.

2. **Reading scalar values** — `GetValue<T>(key)` and `GetValue<T>(key, defaultValue)`. Supported types: `string`, `int`, `bool`, `double`, `TimeSpan`. Note that `TimeSpan` parsing follows ISO 8601 (`"00:00:30"` for 30 seconds).

3. **Reading sections** — `GetSection("SectionName")` returns a sub-`IPluginConfiguration`. Chaining: `config.GetSection("Retry").GetValue<int>("MaxAttempts", 3)`.

4. **Checking existence** — `Exists(key)` for optional features. Enumerate resolved keys via `Keys` for debugging.

5. **Typed binding pattern** — Constructing a typed settings record from `IPluginConfiguration` manually. Full example matching `ConfigDrivenPlugin`.

6. **Hot-reload workaround** — Timer-based re-read pattern. Explicit note: SDK 1.0 has no change notification API. Document the planned `IPluginConfigurationMonitor<T>` for SDK 2.0.

7. **`plugin.config.json` format** — JSON flat and nested examples. File size limit (1 MB). What happens on parse failure (host logs warning, falls back to appsettings-only).

8. **Override via appsettings** — Show the appsettings path format: `"Plugins": { "my.plugin": { "Key": "value" } }`. Confirm that `GetSection` paths are flattened: `GetSection("Retry").GetValue<int>("MaxAttempts")` resolves `Plugins:my.plugin:Retry:MaxAttempts` in appsettings.

---

### `services.md` — Accessing Host Services via IPluginServices

**Sections:**

1. **What `IPluginServices` is** — A restricted view of the host DI container, scoped to services explicitly exposed to plugins. Not the full host `IServiceProvider`.

2. **`GetRequiredService<T>()` vs `GetService<T>()`** — `GetRequiredService<T>()` throws `InvalidOperationException` if not registered; use only for services the plugin cannot function without. `GetService<T>()` returns null; use for optional services.

3. **`GetServices<T>()`** — Returns `IEnumerable<T>`; use when multiple implementations of an interface may be registered.

4. **Services available in SDK 1.0** — Explicitly enumerate what the host registers by default (based on 14B `PluginActivator`):

   | Service type | Availability | Notes |
   |---|---|---|
   | `IPluginLogger` | Always | Also accessible via `Context.Logger` |
   | `IPluginConfiguration` | Always | Also accessible via `Context.Configuration` |
   | `IPluginEnvironment` | Always | Also accessible via `Context.Environment` |
   | `IHttpClientFactory` | Optional | Host registers if ASP.NET Core is used; use `GetService<T>()` |

5. **Pattern for optional services** — Show the null-safe pattern from `WebhookPlugin`: try `GetService<IHttpClientFactory>()`, fall back to `new HttpClient()`, dispose if owned.

6. **What NOT to do** — Cast `IPluginServices` to `IServiceProvider`. Access database contexts. Store the `IPluginServices` reference past `DisposeAsync`.

7. **Extension points (future)** — Forward reference to 14C: when extension point interfaces are added, they will be registered as host services accessible via `GetRequiredService<T>()`. No action needed in SDK 1.0 plugins.

---

### `permissions.md` — PluginPermission Model

**Sections:**

1. **What permissions are** — Declared intent in `plugin.json`. In SDK 1.0, permissions are informational (not enforced at runtime). Enforcement arrives in a future phase.

2. **Permission values** — Full table:

   | Value | Integer | Meaning |
   |---|---|---|
   | `None` | 0 | No special access required |
   | `Collectors` | 1 | Plugin reads from data sources |
   | `Transport` | 2 | Plugin makes outbound network calls |
   | `Operations` | 4 | Plugin performs operational mutations |

3. **Capabilities vs. Permissions** — Capabilities describe what the plugin does (`Collector`, `Transport`, `Operation`, `Router`, `Health`). Permissions describe what host resources it needs. A `Collector` capability typically declares `Collectors` permission; a `Transport` capability declares `Transport` permission.

4. **Declaring permissions in `plugin.json`** — String array format. Unknown strings are logged as warnings and skipped.

5. **Future enforcement model** — In a future phase, the admin must explicitly grant each declared permission before the plugin loads. Plugins that declare permissions not granted by an admin will fail at `ManifestValidation` stage. Prepare now: declare only the permissions you actually need.

6. **Combined permissions** — Plugins may declare multiple permissions. Example: a plugin that polls a database and POSTs results externally declares `["Collectors", "Transport"]`.

---

### `packaging.md` — How to Create a `.msopkg`

**Sections:**

1. **What a `.msopkg` is** — A ZIP archive with a defined internal structure, signed by the plugin author. Required for marketplace submission; optional for local deployment.

2. **`.msopkg` internal layout:**

```
{plugin-id}-{version}.msopkg
├── plugin.json             ← manifest (must be at root)
├── {EntryAssembly}.dll     ← compiled plugin assembly
├── lib/                    ← private dependencies (optional)
│   └── *.dll
├── plugin.config.json      ← default configuration (optional)
├── resources/              ← static assets (optional)
└── signature.sig           ← Ed25519 signature (required for marketplace)
```

3. **Creating the package using the CLI** — `msosync plugin pack ./MyPlugin.csproj --output ./dist`. Show the expected output: `MyPlugin-1.0.0.msopkg created.`

4. **Verifying the package** — `msosync plugin verify ./dist/MyPlugin-1.0.0.msopkg`. Checks: manifest schema, required fields, entry assembly present, no path traversal in filenames.

5. **Signing the package** — `msosync plugin sign ./dist/MyPlugin-1.0.0.msopkg --key ./my-key.pem`. Signing is required for marketplace submission. Unsigned packages may be installed locally with `--allow-unsigned` flag.

6. **Local installation without the marketplace** — Copy the build output directory to `{host}/plugins/{plugin-id}/`. No packaging required. Packaging is only needed for distribution.

7. **NuGet dependency inclusion** — Add `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` to the `.csproj` to ensure private NuGet assemblies are copied to the output directory. These go in `lib/` in the package.

Note: CLI command syntax and flags in sections 3–5 are authoritative once 2C.4 (CLI Tooling) is finalized. If 2C.4 is not yet complete when this doc ships, these sections carry `[CLI: pending 2C.4 finalization]` markers on the command lines only — the surrounding prose and structure are complete.

---

### `publishing.md` — How to Publish to the Marketplace

**Sections:**

1. **Prerequisites** — MSOSync account, signed `.msopkg`, CLI installed.

2. **First-time setup** — `msosync login` authenticates the CLI with the marketplace. Token stored in user profile.

3. **Publishing** — `msosync plugin publish ./dist/MyPlugin-1.0.0.msopkg`. What the marketplace validates on upload: signature, manifest schema, SDK version compatibility range, no blacklisted permissions without prior approval.

4. **Versioning rules** — Semantic versioning strictly enforced. A version once published cannot be overwritten. Use `msosync plugin publish --pre` for pre-release tags.

5. **Marketplace review** — Plugins declaring `Operations` permission are subject to manual review before becoming publicly visible. Estimated review window.

6. **Updating an existing plugin** — Bump `version` in `plugin.json`, rebuild, repack, republish. The marketplace keeps all published versions; users choose upgrade timing.

7. **Deprecating a version** — `msosync plugin deprecate {plugin-id}@{version}`. Deprecated versions remain installable but show a warning in the admin UI.

8. **Plugin page metadata** — The marketplace reads `name`, `description`, `author`, `version`, `capabilities`, and `permissions` from `plugin.json`. A `README.md` at the package root is displayed as the plugin's marketplace landing page if present.

Note: Same CLI pending marker policy as `packaging.md`.

---

### `api-reference.md` — All SDK Interfaces Documented

**Structure:** One H2 section per interface or type. No prose introduction — this is a reference document.

**Sections (in order):**

1. **`IPlugin`** — Method signatures, parameter semantics, return value contract, exception behavior, cancellation contract for each of the three methods plus `DisposeAsync`.

2. **`IPluginContext`** — Property-by-property documentation. What each property provides, when it becomes available (after `InitializeAsync` call, not before), thread safety guarantee.

3. **`IPluginLogger`** — Each method: signature, log level it corresponds to, when to use vs. the alternatives, `BeginScope` scope lifetime and correct `using` pattern.

4. **`IPluginConfiguration`** — Each method and property. Type conversion behavior for `GetValue<T>`. `GetSection` nesting semantics. `Keys` enumeration ordering (unspecified — do not depend on order). `Exists` semantics when a key is present but null-valued.

5. **`IPluginServices`** — Each method. `GetRequiredService<T>` vs `GetService<T>` vs `GetServices<T>`. Which types are guaranteed to be registered. Thread safety (resolved at activation time; safe to call from any plugin method).

6. **`IPluginEnvironment`** — Each property. `EnvironmentName` possible values (`"Development"`, `"Production"`, `"Staging"`). `HostVersion` format (semantic version string). `DataDirectory` and `PluginDirectory` path guarantees (absolute, normalized, exist at call time).

7. **`PluginBase`** — Class-level documentation. `Context` property: available after `InitializeAsync` returns (not in the constructor). Default implementations and their return values.

8. **`PluginMetadata`** — Each property with its source (populated from `plugin.json` fields at activation).

9. **`PluginCapability`** — Each enum value with description and when to declare it.

10. **`PluginPermission`** — Each enum value with description, what host resources it implies access to, and enforcement status.

---

## Global Constraints

### No internal implementation references in samples

All sample code must compile using only the public surface of `MSOSync.Sdk`. No `using MSOSync.Plugin`, `using MSOSync.Api`, `using MSOSync.Metadata`, `using MSOSync.Persistence`, or `using MSOSync.Common` in any sample file. Enforced by the fact that sample `.csproj` files do not reference any project other than `MSOSync.Sdk`.

### No runtime coupling to host internals

Samples must run in isolation — they must not assume any specific host service beyond what `IPluginContext` provides. `WebhookPlugin`'s use of `IHttpClientFactory` is optional (null-checked); it is the only host service reference in any sample.

### Warning-clean build

All samples and templates must build with zero compiler warnings under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. This is enforced by the CI `build-check.ps1` script which uses `--warnaserror`.

### Markdown only for the portal

No HTML, no front-matter YAML, no shortcodes. All code blocks use fenced syntax with explicit language tags (`csharp`, `json`, `bash`, `powershell`, `text`). All cross-references use relative links (`[configuration guide](configuration.md)`) so the portal renders correctly both on GitHub and in local Markdown viewers.

### Stable SDK surface assumption

This spec is written against SDK 1.0 as finalized in Epic 14B. If any interface in `MSOSync.Sdk` changes before 2C.5 is implemented, the spec author must review all affected samples and portal pages before beginning implementation.

### No `MSOSync.sln` modification

The `MSOSync.Templates` project is added to `MSOSync.sln` because it is a `src/` project that ships as a NuGet package. The four sample projects under `samples/` are NOT added to `MSOSync.sln` — they are built only by `samples/build-check.ps1`.

### `MSOSync.Templates` added to solution

`src/MSOSync.Templates/MSOSync.Templates.csproj` IS added to `MSOSync.sln` under a `Templates` solution folder. It is excluded from the default build configuration (`Build.0 = Release|Any CPU` entry absent) so it does not build on every `dotnet build MSOSync.sln`. A separate CI step builds and packs it explicitly.

---

## Testing Approach

### Sample build gate

`samples/build-check.ps1` runs in CI as a required check. It must pass before any 2C.5 branch can merge. The script builds each sample in the sequence listed, accumulates failures, and reports all failures at the end rather than stopping at the first. This ensures a PR author sees all broken samples at once.

### Template scaffold and build test

A second CI script (`src/MSOSync.Templates/test-templates.ps1`) runs after `dotnet new install`:

1. Scaffold `msosync-plugin` with name `TestBasicPlugin` into a temp directory.
2. Run `dotnet build` on the scaffolded project. Must exit 0.
3. Scaffold `msosync-plugin-advanced` with name `TestAdvancedPlugin` and `--capability Collector` into a temp directory.
4. Run `dotnet build` on the scaffolded project. Must exit 0.
5. Clean up temp directories.

### Portal link check

A lightweight PowerShell script validates that every `[text](filename.md)` link in each portal file resolves to a file that exists in `docs/developer-portal/`. External links are excluded from validation. Runs in CI.

### No unit tests for samples

Samples are not test-covered beyond the build gate. They are illustrative, not production code. The build gate ensures they compile; the README content is reviewed by humans during PR review.

### Manual validation gate before merge

Before the 2C.5 branch merges:

1. At least one developer has followed `getting-started.md` from scratch against a real MSOSync host instance and confirmed the plugin loads.
2. Both templates have been installed and scaffolded on a clean machine (not the development machine).
3. All eight portal pages have been reviewed for broken links and technical accuracy against the actual SDK interfaces.
