# Task 1: HelloWorldPlugin + DataCollectorPlugin

**Status:** Ready  
**Estimated time:** 6 hours  
**Dependencies:** None  
**Blocks:** Task 3 (Templates)

---

## Summary

Implement two complete sample plugins demonstrating the minimal lifecycle contract and real-world data collection with configuration. Each includes `.csproj`, implementation, manifests, config, and README.

---

## Part A: HelloWorldPlugin

### Step 1.1 — Create HelloWorldPlugin directory structure

```powershell
$root = "D:\MSOSync"
$hello = "$root\samples\HelloWorldPlugin"

if (-not (Test-Path $hello)) {
  New-Item -ItemType Directory -Force $hello | Out-Null
}
Write-Host "Created $hello"
```

**Verify:** Directory exists and is empty.

### Step 1.2 — Create HelloWorldPlugin.csproj

**File:** `samples/HelloWorldPlugin/HelloWorldPlugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

**Why:** Minimal .csproj — inherits framework, language, and warning settings from `Directory.Build.props`.

### Step 1.3 — Create HelloWorldPlugin.cs

**File:** `samples/HelloWorldPlugin/HelloWorldPlugin.cs`

```csharp
using MSOSync.Sdk.Hosting;

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

**Key points:**
- Extends `PluginBase` (overrides only `StartAsync`, inherits no-op defaults for others)
- Uses `Context.Logger.BeginScope()` for structured logging
- Accesses `Context.Metadata` (PluginId, Version)
- Accesses `Context.Environment` (HostVersion, EnvironmentName, IsDevelopment)
- Respects `IsDevelopment` flag before logging debug info
- Returns `Task.CompletedTask` immediately (non-blocking)

### Step 1.4 — Create plugin.json manifest

**File:** `samples/HelloWorldPlugin/plugin.json`

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

### Step 1.5 — Create plugin.config.json

**File:** `samples/HelloWorldPlugin/plugin.config.json`

```json
{}
```

### Step 1.6 — Create HelloWorldPlugin README.md

**File:** `samples/HelloWorldPlugin/README.md`

```markdown
# HelloWorldPlugin

The minimal complete plugin sample. Demonstrates the absolute minimum a developer must implement to have a working plugin accepted by the MSOSync plugin host.

## What This Sample Teaches

- Extending `PluginBase` instead of implementing `IPlugin` directly
- Overriding only the lifecycle methods you need (here: `StartAsync`)
- Using `IPluginContext.Logger` for structured logging
- Reading plugin metadata from `IPluginContext.Metadata`
- Checking environment via `IPluginContext.Environment.IsDevelopment`

## Building

```bash
cd samples/HelloWorldPlugin
dotnet build
```

Expected output: `Build succeeded in X.XXXs`

## Plugin Lifecycle

This plugin demonstrates the minimal lifecycle:

```
Host loads plugin
        ↓
    InitializeAsync (default: no-op, Context is cached)
        ↓
    StartAsync (logs "Hello World" line)
        ↓
    Plugin Running
        ↓
    StopAsync (default: no-op)
        ↓
    DisposeAsync (default: no-op)
```

## Running Against a Host

1. Build this plugin: `dotnet build`
2. Copy the output to the host's plugin directory: `{host}/plugins/samples.hello-world/`
3. Restart the MSOSync host
4. Check the host logs for:
   - `PluginHost1002: Plugin samples.hello-world loaded successfully`
   - `Hello World from samples.hello-world v1.0.0 (host: 14.x.x, env: Production)`

## Configuration

This plugin does not require configuration. The `plugin.config.json` is empty.

## Key Concepts Demonstrated

| Concept | Code | Purpose |
|---------|------|---------|
| `PluginBase` | `class HelloWorldPlugin : PluginBase` | Convenience class with default implementations |
| `Context` | `Context.Logger`, `Context.Metadata` | Access to host services and plugin metadata |
| Logging | `Context.Logger.LogInformation(...)` | Emit structured log lines |
| Metadata | `Context.Metadata.PluginId` | Read your own plugin ID, name, version |
| Environment | `Context.Environment.IsDevelopment` | Branch behavior by environment |

## Next Steps

- See [DataCollectorPlugin](../DataCollectorPlugin/README.md) for real work on a timer
- See [WebhookPlugin](../WebhookPlugin/README.md) for integrating with external systems
- See [ConfigDrivenPlugin](../ConfigDrivenPlugin/README.md) for configuration patterns
```

### Step 1.7 — Verify HelloWorldPlugin builds

```powershell
cd D:\MSOSync\samples\HelloWorldPlugin
dotnet build --warnaserror
```

**Expected:** Exit code 0, zero warnings.

- [ ] HelloWorldPlugin builds successfully

---

## Part B: DataCollectorPlugin

### Step 1.8 — Create DataCollectorPlugin directory structure

```powershell
$root = "D:\MSOSync"
$collector = "$root\samples\DataCollectorPlugin"

if (-not (Test-Path $collector)) {
  New-Item -ItemType Directory -Force $collector | Out-Null
}
Write-Host "Created $collector"
```

### Step 1.9 — Create DataCollectorPlugin.csproj

**File:** `samples/DataCollectorPlugin/DataCollectorPlugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.1.5" />
  </ItemGroup>
</Project>
```

**Note:** Adds `Microsoft.Data.SqlClient` as a NuGet dependency — the only sample with a private external dependency.

### Step 1.10 — Create MetricSample.cs

**File:** `samples/DataCollectorPlugin/MetricSample.cs`

```csharp
namespace DataCollectorPlugin;

internal sealed record MetricSample(
    string TableName,
    int RowCount,
    DateTime CollectedAt);
```

### Step 1.11 — Create DataCollectorPlugin.cs

**File:** `samples/DataCollectorPlugin/DataCollectorPlugin.cs`

```csharp
using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using MSOSync.Sdk.Hosting;

namespace DataCollectorPlugin;

public sealed class DataCollectorPlugin : PluginBase
{
    private Timer? _pollingTimer;
    private readonly ConcurrentQueue<MetricSample> _metrics = new();

    public override Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Context = context;

        // Validate configuration at init time
        var connStr = Context.Configuration.GetValue<string>("ConnectionString", "");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            Context.Logger.LogWarning("No ConnectionString in configuration; polling will not run");
        }

        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("DataCollectorPlugin.Start");

        var intervalSeconds = Context.Configuration
            .GetSection("Polling")
            .GetValue("IntervalSeconds", 30);

        Context.Logger.LogInformation(
            "Data collector starting (PluginId: {PluginId}, PollInterval: {Interval}s)",
            Context.Metadata.PluginId,
            intervalSeconds);

        _pollingTimer = new Timer(
            _ => PollDatabase(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(intervalSeconds));

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        using var scope = Context.Logger.BeginScope("DataCollectorPlugin.Stop");
        Context.Logger.LogInformation("Data collector stopping");
        return Task.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        _pollingTimer?.Dispose();
        await base.DisposeAsync();
    }

    private void PollDatabase()
    {
        try
        {
            var connStr = Context.Configuration.GetValue<string>("ConnectionString", "");
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return;
            }

            var tableName = Context.Configuration
                .GetSection("Polling")
                .GetValue("TableName", "dbo.SyncEvents");

            using var conn = new SqlConnection(connStr);
            conn.Open();

            var query = $"SELECT COUNT(*) FROM {tableName}";
            using var cmd = new SqlCommand(query, conn);
            cmd.CommandTimeout = 10;

            var count = (int?)cmd.ExecuteScalar() ?? 0;

            var sample = new MetricSample(tableName, count, DateTime.UtcNow);
            _metrics.Enqueue(sample);

            Context.Logger.LogDebug(
                "Collected metric: {TableName} has {RowCount} rows",
                tableName,
                count);

            if (count > 10000)
            {
                Context.Logger.LogWarning(
                    "High row count detected: {TableName} = {RowCount}",
                    tableName,
                    count);
            }
        }
        catch (SqlException ex)
        {
            Context.Logger.LogError(ex, "SQL error during polling");
        }
        catch (Exception ex)
        {
            Context.Logger.LogError(ex, "Unexpected error during polling");
        }
    }
}
```

**Key points:**
- `InitializeAsync`: Validates configuration at startup (expensive I/O belongs here)
- `StartAsync`: Creates `System.Threading.Timer` with configured interval
- `StopAsync`: Explicit log on shutdown
- `DisposeAsync`: Disposes timer
- `PollDatabase`: Private method handling SQL errors gracefully (never throws)
- Uses `ConcurrentQueue<T>` for thread-safe metric accumulation
- Logs at appropriate levels: `Debug` for per-cycle, `Warning` for elevated counts, `Error` for exceptions

### Step 1.12 — Create plugin.json manifest

**File:** `samples/DataCollectorPlugin/plugin.json`

```json
{
  "manifestVersion": 1,
  "id": "samples.data-collector",
  "name": "Data Collector Plugin",
  "version": "1.0.0",
  "sdkVersion": "1.0",
  "apiVersion": "1",
  "startupOrder": 1000,
  "minHostVersion": "14.0.0",
  "maxHostVersion": "14.9.999",
  "entryAssembly": "DataCollectorPlugin.dll",
  "entryType": "DataCollectorPlugin.DataCollectorPlugin",
  "author": "MSOSync",
  "description": "Data collector sample — demonstrates IPluginConfiguration, background timers, and SQL polling.",
  "permissions": ["Collectors"],
  "dependencies": [],
  "capabilities": ["Collector"]
}
```

### Step 1.13 — Create plugin.config.json

**File:** `samples/DataCollectorPlugin/plugin.config.json`

```json
{
  "ConnectionString": "Server=localhost;Database=MyDb;Trusted_Connection=True;",
  "Polling": {
    "IntervalSeconds": 30,
    "TableName": "dbo.SyncEvents"
  }
}
```

### Step 1.14 — Create DataCollectorPlugin README.md

**File:** `samples/DataCollectorPlugin/README.md`

```markdown
# DataCollectorPlugin

A plugin that polls a SQL Server database on a configured interval, accumulates metrics, and demonstrates real background work, configuration reading, and exception handling.

## What This Sample Teaches

- Reading configuration via `IPluginConfiguration.GetValue<T>()` and `GetSection()`
- Starting and stopping a `System.Threading.Timer` in `StartAsync`/`StopAsync`
- Handling SQL exceptions gracefully without crashing the plugin
- Thread-safe metric accumulation with `ConcurrentQueue<T>`
- Declaring `PluginCapability.Collector` and `PluginPermission.Collectors`
- Using private NuGet dependencies (`Microsoft.Data.SqlClient`) in `lib/`

## Building

```bash
cd samples/DataCollectorPlugin
dotnet build
```

Expected output: `Build succeeded in X.XXXs`

## Configuration

All configuration is read from `plugin.config.json` (low priority) or the host's `appsettings.json` under `Plugins:samples.data-collector:*` (high priority).

### Configuration Keys

| Key | Type | Default | Description |
|-----|------|---------|---|
| `ConnectionString` | `string` | (empty) | SQL Server connection string (required) |
| `Polling:IntervalSeconds` | `int` | 30 | Poll interval in seconds |
| `Polling:TableName` | `string` | `dbo.SyncEvents` | Table to count rows from |

### Example: Override via appsettings.json

```json
{
  "Plugins": {
    "samples.data-collector": {
      "ConnectionString": "Server=prod-sql;Database=SyncDb;Integrated Security=true;",
      "Polling": {
        "IntervalSeconds": 60
      }
    }
  }
}
```

## Running Against a Host

1. Build this plugin: `dotnet build`
2. Configure a SQL Server connection string:
   - Either modify `plugin.config.json` with your server details
   - Or add to host's `appsettings.json` (shown above)
3. Copy the output to the host's plugin directory: `{host}/plugins/samples.data-collector/`
4. Restart the MSOSync host
5. Check the host logs for:
   - `PluginHost1002: Plugin samples.data-collector loaded successfully`
   - `Data collector starting (PluginId: samples.data-collector, PollInterval: 30s)`
   - (every 30 seconds) `Collected metric: dbo.SyncEvents has NNNN rows`

## Private Dependencies

This plugin includes `Microsoft.Data.SqlClient` as a private dependency. When packaged with `msosync plugin pack`, the DLL goes into the `lib/` folder of the `.msopkg` and is loaded alongside the plugin assembly.

The `.csproj` includes:

```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.1.5" />
```

To ensure private dependencies are copied on build, the project uses the project-wide default from `Directory.Build.props` (no explicit override needed here).

## Thread Safety

- `_metrics` is a `ConcurrentQueue<T>` — thread-safe for both enqueue and enumeration
- `_pollingTimer` is disposed in `DisposeAsync`
- The plugin maintains no other mutable state

## Exception Handling

The `PollDatabase()` method catches SQL and general exceptions separately:

- `SqlException`: Logged as error, timer continues
- `Exception`: Logged as error, timer continues

The plugin never throws — if SQL fails, it logs and the host continues running.

## Key Concepts Demonstrated

| Concept | Code | Purpose |
|---------|------|---------|
| `InitializeAsync` | Validate connection string | Fail fast at init, not at runtime |
| `GetSection` | `GetSection("Polling")` | Navigate nested configuration |
| `GetValue<T>` | `GetValue<int>("IntervalSeconds", 30)` | Read scalar with default |
| `Timer` | `new Timer(_ => PollDatabase(), ...)` | Background polling |
| `ConcurrentQueue<T>` | Store metrics thread-safely | |
| Exception handling | Catch, log, continue | Never fail the host |

## Next Steps

- See [WebhookPlugin](../WebhookPlugin/README.md) for HTTP delivery patterns
- See [ConfigDrivenPlugin](../ConfigDrivenPlugin/README.md) for advanced configuration patterns
```

### Step 1.15 — Verify DataCollectorPlugin builds

```powershell
cd D:\MSOSync\samples\DataCollectorPlugin
dotnet build --warnaserror
```

**Expected:** Exit code 0, zero warnings.

- [ ] DataCollectorPlugin builds successfully

---

## Step 1.16 — Final Verification

```powershell
$root = "D:\MSOSync"
$helloProj = "$root\samples\HelloWorldPlugin\HelloWorldPlugin.csproj"
$collectorProj = "$root\samples\DataCollectorPlugin\DataCollectorPlugin.csproj"

# Verify files exist
$files = @(
  "$root\samples\HelloWorldPlugin\HelloWorldPlugin.cs",
  "$root\samples\HelloWorldPlugin\plugin.json",
  "$root\samples\HelloWorldPlugin\plugin.config.json",
  "$root\samples\HelloWorldPlugin\README.md",
  "$root\samples\DataCollectorPlugin\DataCollectorPlugin.cs",
  "$root\samples\DataCollectorPlugin\MetricSample.cs",
  "$root\samples\DataCollectorPlugin\plugin.json",
  "$root\samples\DataCollectorPlugin\plugin.config.json",
  "$root\samples\DataCollectorPlugin\README.md"
)

foreach ($file in $files) {
  if (Test-Path $file) {
    Write-Host "✓ $file"
  } else {
    Write-Error "✗ $file NOT FOUND"
  }
}

# Build both
Write-Host "`nBuilding HelloWorldPlugin..."
dotnet build $helloProj /p:MSOSyncSdkLocal=true --warnaserror
if ($LASTEXITCODE -ne 0) { Write-Error "HelloWorldPlugin build failed"; exit 1 }

Write-Host "`nBuilding DataCollectorPlugin..."
dotnet build $collectorProj /p:MSOSyncSdkLocal=true --warnaserror
if ($LASTEXITCODE -ne 0) { Write-Error "DataCollectorPlugin build failed"; exit 1 }

Write-Host "`nTask 1 verification complete!"
```

- [ ] All files exist
- [ ] Both samples compile with zero errors and zero warnings

**Next:** Proceed to Task 2 (WebhookPlugin + ConfigDrivenPlugin)
