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
