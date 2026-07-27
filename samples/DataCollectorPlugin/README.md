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
<PackageReference Include="Microsoft.Data.SqlClient" />
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
