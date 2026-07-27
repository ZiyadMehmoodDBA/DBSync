# ConfigDrivenPlugin

The authoritative example of `IPluginConfiguration`. Demonstrates typed configuration binding, section navigation, defaults, existence checks, and the hot-reload pattern.

## What This Sample Teaches

- `IPluginConfiguration.GetValue<T>(key, defaultValue)` for scalars with defaults
- `IPluginConfiguration.GetSection("SectionName")` for nested config objects
- `IPluginConfiguration.Keys` enumeration for debugging
- Manual typed binding pattern (constructing a record from config sections)
- Hot-reload via timer-based polling (workaround for lack of change notifications in SDK 1.0)
- Configuration priority: host `appsettings.json` wins over `plugin.config.json`

## Building

```bash
cd samples/ConfigDrivenPlugin
dotnet build
```

Expected output: `Build succeeded in X.XXXs`

## Configuration

This plugin reads from `plugin.config.json` and the host's `appsettings.json` (`Plugins:samples.config-driven:*`).

### Configuration Structure

```
Feature
  ├── EnableDetailedLogging (bool)
  └── MaxBatchSize (int)
Retry
  ├── MaxAttempts (int)
  └── DelayMs (int)
Thresholds
  ├── WarnAtQueueDepth (int)
  └── ErrorAtQueueDepth (int)
```

### Configuration Keys

| Key | Type | Default | Description |
|-----|------|---------|---|
| `Feature:EnableDetailedLogging` | bool | false | Enable verbose logging |
| `Feature:MaxBatchSize` | int | 100 | Max items per batch |
| `Retry:MaxAttempts` | int | 3 | Max retry attempts |
| `Retry:DelayMs` | int | 1000 | Delay between retries (ms) |
| `Thresholds:WarnAtQueueDepth` | int | 1000 | Log warning above this depth |
| `Thresholds:ErrorAtQueueDepth` | int | 5000 | Log error above this depth |

### Example: Override via appsettings.json

```json
{
  "Plugins": {
    "samples.config-driven": {
      "Feature": {
        "EnableDetailedLogging": false,
        "MaxBatchSize": 1000
      },
      "Retry": {
        "MaxAttempts": 5,
        "DelayMs": 2000
      }
    }
  }
}
```

Note: `Thresholds` is not overridden, so it uses the values from `plugin.config.json`.

## Running Against a Host

1. Build this plugin: `dotnet build`
2. Optionally configure values via host's `appsettings.json` (see above)
3. Copy the output to the host's plugin directory: `{host}/plugins/samples.config-driven/`
4. Restart the MSOSync host
5. Check the host logs for:
   - `PluginHost1002: Plugin samples.config-driven loaded successfully`
   - `Configuration keys resolved at startup: N`
   - (if Development environment) list of all keys
   - (every 30 seconds) log lines for any changed values

## Typed Binding Pattern

The `PluginSettings` record is constructed manually from configuration sections:

```csharp
private PluginSettings LoadSettings()
{
    var featureSection = Context.Configuration.GetSection("Feature");
    var retrySection = Context.Configuration.GetSection("Retry");
    var thresholdSection = Context.Configuration.GetSection("Thresholds");

    return new PluginSettings(
        EnableDetailedLogging: featureSection.GetValue("EnableDetailedLogging", false),
        MaxBatchSize: featureSection.GetValue("MaxBatchSize", 100),
        RetryMaxAttempts: retrySection.GetValue("MaxAttempts", 3),
        // ... etc
    );
}
```

There is no `Bind<T>` method on `IPluginConfiguration` in SDK 1.0 — manual binding is the canonical pattern.

## Hot-Reload Pattern (Workaround)

SDK 1.0 does not provide `IPluginConfigurationMonitor<T>`. This plugin implements a workaround:

1. Start a `Timer` that fires every 30 seconds
2. Call `LoadSettings()` to re-read configuration
3. Compare new settings to cached settings
4. Log changes when detected

This is not a perfect solution (changes are detected with ~30-second lag), but it demonstrates the pattern developers can use today.

**Future:** SDK 2.0 will add `IPluginConfigurationMonitor<T>` with change notifications.

## Configuration Priority

When reading configuration, the host applies this priority:

1. **High:** `appsettings.json` under `Plugins:samples.config-driven:*`
2. **Low:** `plugin.config.json` in the plugin directory

If a key exists in appsettings.json, it wins. Otherwise, plugin.config.json is used. If a key exists in neither, the default value provided to `GetValue<T>(key, default)` is used.

Example: if both files have `MaxBatchSize`:
- appsettings.json: `MaxBatchSize: 1000`
- plugin.config.json: `MaxBatchSize: 500`
- Result: `1000` (appsettings.json wins)

## Key Concepts Demonstrated

| Concept | Code | Purpose |
|---------|------|---------|
| `GetSection` | `GetSection("Feature")` | Navigate nested sections |
| `GetValue<T>` | `GetValue("MaxBatchSize", 100)` | Read with default |
| `Keys` | Enumerate all keys | Debug configuration mismatches |
| Typed binding | Manual record construction | Type-safe configuration |
| Hot-reload | Timer polling + comparison | Detect config changes at runtime |
| Priority | appsettings.json > plugin.config.json | Understand which config wins |

## Next Steps

- See [WebhookPlugin](../WebhookPlugin/README.md) for optional service resolution
- See [DataCollectorPlugin](../DataCollectorPlugin/README.md) for background timers with configuration
