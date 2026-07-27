# Getting Started with MSOSync Plugins

Welcome! This guide will get you from zero to your first running plugin in under 5 minutes.

## Prerequisites

- **.NET 9 SDK** or later installed
- **MSOSync host** installed and running
- **`dotnet new` CLI** available (included with .NET SDK)

## Step 1: Install the Template

Install the MSOSync plugin templates:

```bash
dotnet new install MSOSync.Templates
```

This registers two templates with the `dotnet` CLI:
- `msosync-plugin` — Basic template for a minimal plugin
- `msosync-plugin-advanced` — Template with configuration and service integration

## Step 2: Scaffold Your First Plugin

Create a new plugin project:

```bash
dotnet new msosync-plugin --name MyFirstPlugin
cd MyFirstPlugin
```

The template generates these files:

```
MyFirstPlugin/
├── MyFirstPlugin.csproj
├── MyFirstPlugin.cs
├── plugin.json
├── plugin.config.json
└── (bin/, obj/ after build)
```

### Customize (Optional)

The template accepts additional parameters:

```bash
dotnet new msosync-plugin \
  --name AwesomeCollector \
  --pluginId acme.awesome-collector \
  --author "Acme Corp" \
  --description "Collects awesome metrics"
```

## Step 3: Build and Verify

```bash
dotnet build
```

Expected output:

```
Build succeeded in 2.345s
```

If you see warnings, check your code — the SDK enforces zero warnings.

## Step 4: Drop Into the Host

Deploy the plugin to your MSOSync host. Copy the build output directory to the host's plugins folder:

```bash
# Windows PowerShell
Copy-Item -Recurse .\bin\Release\net9.0\* -Destination "{host-path}\plugins\my-first-plugin"

# macOS/Linux bash
cp -r ./bin/Release/net9.0/* {host-path}/plugins/my-first-plugin/
```

The host expects this directory layout:

```
{host}/plugins/my-first-plugin/
├── MyFirstPlugin.dll
├── plugin.json
├── plugin.config.json
└── (any private dependencies in lib/ subdirectory)
```

## Step 5: Restart and Verify

Restart the MSOSync host:

```bash
# Assuming host runs as systemd service (Linux)
sudo systemctl restart msosync

# Or if running as a console app, stop and re-run it
```

Watch the host logs for success indicators:

```
[INFO] PluginHost1002: Plugin my-first-plugin loaded successfully
[INFO] MyFirstPlugin started (host: 14.0.0)
```

If you see these lines, your plugin is running!

## Troubleshooting

### Plugin fails to load
- Check `plugin.json` fields are valid (see [Plugin Lifecycle](plugin-lifecycle.md))
- Ensure `entryAssembly` matches your DLL name exactly
- Verify the host can write to the plugin directory (permissions)

### Build fails with warnings as errors
- Check the build output for `warning:` lines
- Common issues:
  - Unused `using` statements
  - Nullable reference warnings (initialize all fields)
  - Unreachable code

## Next Steps

Now that your first plugin runs, explore:

- **[Plugin Lifecycle](plugin-lifecycle.md)** — Understand `InitializeAsync`, `StartAsync`, `StopAsync`, `DisposeAsync`
- **[Configuration](configuration.md)** — Read settings from `plugin.config.json` and host `appsettings.json`
- **[Services](services.md)** — Access host-provided services like `IHttpClientFactory`
- **[Official Samples](../../samples/)** — See complete implementations of Collector, Transport, and Configuration patterns

## Using the Advanced Template

For more complex plugins, use the advanced template:

```bash
dotnet new msosync-plugin-advanced --name MyCollector --capability Collector
```

This scaffolds:
- Typed `Settings` record for configuration binding
- `InitializeAsync` for initialization logic
- Timer-based background work in `StartAsync`
- Proper `DisposeAsync` cleanup

See the advanced template's generated comments for guidance.
