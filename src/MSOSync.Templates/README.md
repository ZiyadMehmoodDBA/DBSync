# MSOSync.Templates

Official `dotnet new` templates for building MSOSync plugins.

## Installation

From NuGet:
```bash
dotnet new install MSOSync.Templates
```

From local package (development):
```bash
dotnet pack src/MSOSync.Templates/MSOSync.Templates.csproj -o ./artifacts
dotnet new install ./artifacts/MSOSync.Templates.1.0.0.nupkg
```

## Available Templates

### msosync-plugin (Basic)

Scaffolds a minimal plugin extending `PluginBase`.

```bash
dotnet new msosync-plugin --name MyPlugin
```

Parameters:
- `--name` (required): Plugin class name and project name
- `--pluginId`: Reverse-DNS plugin ID (default: `my.plugin`)
- `--author`: Plugin author name (default: `My Organization`)
- `--description`: Short plugin description

### msosync-plugin-advanced (Config + Services)

Scaffolds a plugin with typed configuration binding and service resolution.

```bash
dotnet new msosync-plugin-advanced --name MyCollector --capability Collector
```

Parameters:
- `--name` (required): Plugin class name and project name
- `--pluginId`: Reverse-DNS plugin ID (default: `my.plugin`)
- `--author`: Plugin author name (default: `My Organization`)
- `--description`: Short plugin description
- `--capability`: Plugin capability (`None`, `Collector`, `Transport`, `Operation`)

## Listing Templates

```bash
dotnet new list --tag MSOSync
```

Expected output:
```
Templates                           Short Name              Language  Tags
msosync-plugin                      msosync-plugin          [C#]      MSOSync/Plugin
msosync-plugin-advanced             msosync-plugin-advanced [C#]      MSOSync/Plugin
```
