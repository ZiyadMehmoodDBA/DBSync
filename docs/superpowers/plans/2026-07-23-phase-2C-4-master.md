# Phase 2C.4 — CLI Tooling (`msosync` global tool) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Date:** 2026-07-23
**Phase:** 2C — SDK & Ecosystem
**Deliverable:** `msosync` .NET 9 global tool — plugin scaffolding, packaging, publishing, and server management

**Goal:** A standalone `msosync` .NET global tool enabling plugin developers to scaffold, pack, publish `.msopkg` packages, and interact with a running MSOSync server. Zero dependency on ASP.NET Core, EF Core, or DI container. No reference to `MSOSync.Api`, `MSOSync.Plugin`, `MSOSync.Persistence`, `MSOSync.Common`, or `MSOSync.App`.

**Tech Stack:** C# 13 / .NET 9 / `System.CommandLine` 2.0.0-beta4.22272.1 / xUnit + `FakeHttpMessageHandler` (no Moq)

---

## Global Constraints

- `PackAsTool=true`, `ToolCommandName=msosync` — installs as a .NET global tool
- **Only project reference allowed:** `MSOSync.Sdk` — used for `PluginBase`, `PluginCapability`, `PluginPermission`
- `PluginManifest` does NOT exist in `MSOSync.Sdk`; the CLI defines its own `CliPluginManifest` record in `MSOSync.Cli.Packaging`
- No `Microsoft.Extensions.DependencyInjection` — dependencies constructed manually in each command
- No `Microsoft.AspNetCore.*` — pure console executable
- No `Microsoft.EntityFrameworkCore.*`
- Exit codes: `0` success, `1` operational error, `2` validation failure
- `[OK]`/`[WRN]`/info → `Console.Out`; `[ERR]` → `Console.Error` (pipe-compatible)
- Stack traces suppressed from user output; shown only when `MSOSYNC_CLI_DEBUG=1`
- `System.CommandLine` is not in `Directory.Packages.props` — add it there; do not pin version in `.csproj`
- Test project: xUnit only, no Moq — mock HTTP via `FakeHttpMessageHandler`
- All projects inherit `net9.0`, `LangVersion 13.0`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true` from `Directory.Build.props`
- Package versions managed centrally in `Directory.Packages.props` — no explicit versions in individual `.csproj` files
- `dotnet sln add` places projects under the existing `src` and `tests` solution folders

---

## Tasks

| # | Task | File |
|---|------|------|
| 1 | Project scaffold — csproj, solution wiring, `Directory.Packages.props` update, `CliConfig`/`CliConfigStore`, `CliConsole`, `MsoSyncHttpClient`, `FakeHttpMessageHandler`, test project | [task-1](2026-07-23-phase-2C-4-task-1-scaffold.md) |
| 2 | `plugin new` + `plugin pack` commands — `PluginScaffolder`, `CliPluginManifest`, `PluginPacker`, `PackageSigningService`, templates, command wiring | [task-2](2026-07-23-phase-2C-4-task-2-new-pack.md) |
| 3 | `plugin publish` + `plugin install` + `plugin list` commands — HTTP calls, response parsing, error handling, tests | [task-3](2026-07-23-phase-2C-4-task-3-publish-install-list.md) |
| 4 | `server health` command, `Program.cs`, solution build verification, `dotnet tool install` smoke test, all tests green | [task-4](2026-07-23-phase-2C-4-task-4-health-wire.md) |

---

## Project Layout

```
src/MSOSync.Cli/
├── MSOSync.Cli.csproj
├── Program.cs
├── Commands/
│   ├── PluginNewCommand.cs
│   ├── PluginPackCommand.cs
│   ├── PluginPublishCommand.cs
│   ├── PluginInstallCommand.cs
│   ├── PluginListCommand.cs
│   └── ServerHealthCommand.cs
├── Config/
│   ├── CliConfig.cs
│   └── CliConfigStore.cs
├── Http/
│   └── MsoSyncHttpClient.cs
├── Output/
│   └── CliConsole.cs
├── Packaging/
│   ├── CliPluginManifest.cs
│   ├── PluginPacker.cs
│   └── PackageSigningService.cs
└── Scaffolding/
    ├── PluginScaffolder.cs
    └── Templates/
        ├── Plugin.csproj.template
        ├── PluginImpl.cs.template
        ├── plugin.json.template
        └── plugin.config.json.template

tests/MSOSync.CliTests/
├── MSOSync.CliTests.csproj
├── Helpers/
│   └── FakeHttpMessageHandler.cs
├── Commands/
│   ├── PluginNewCommandTests.cs
│   ├── PluginPackCommandTests.cs
│   ├── PluginPublishCommandTests.cs
│   ├── PluginInstallCommandTests.cs
│   ├── PluginListCommandTests.cs
│   └── ServerHealthCommandTests.cs
├── Config/
│   └── CliConfigStoreTests.cs
└── Packaging/
    └── PluginPackerTests.cs
```

---

## Command Tree

```
msosync
├── plugin
│   ├── new <name> [--output <dir>] [--author <author>] [--description <desc>]
│   ├── pack [--output <dir>] [--configuration <cfg>] [--sign-key <path>]
│   ├── publish <file> [--registry <url>] [--api-key <key>]
│   ├── install <id[@version]> [--server <url>] [--token <jwt>]
│   └── list [--server <url>] [--token <jwt>]
└── server
    └── health [--server <url>]
```

---

## Config Schema (`~/.msosync/config.json`)

```json
{
  "serverUrl":      "http://localhost:5000",
  "serverToken":    "",
  "registryUrl":    "https://marketplace.msosync.io",
  "registryApiKey": "",
  "signingKeyPath": ""
}
```

Precedence: CLI flag > `config.json` > hardcoded defaults in `CliConfig`.

---

## Exit Code Reference

| Code | Meaning |
|------|---------|
| `0`  | Success |
| `1`  | Operational error (network, auth, server, build failure) |
| `2`  | Validation failure (bad input, format, manifest, not found) |

---

## Key Type Cross-Reference

```csharp
// MSOSync.Cli/Config/CliConfig.cs
public sealed record CliConfig
{
    public string ServerUrl      { get; init; } = "http://localhost:5000";
    public string ServerToken    { get; init; } = string.Empty;
    public string RegistryUrl    { get; init; } = "https://marketplace.msosync.io";
    public string RegistryApiKey { get; init; } = string.Empty;
    public string SigningKeyPath { get; init; } = string.Empty;
}

// MSOSync.Cli/Packaging/CliPluginManifest.cs
// Local copy of manifest schema — avoids reference to MSOSync.Plugin
public sealed record CliPluginManifest
{
    [JsonPropertyName("manifestVersion")] public int    ManifestVersion { get; init; } = 1;
    [JsonPropertyName("id")]              public string Id              { get; init; } = null!;
    [JsonPropertyName("name")]            public string Name            { get; init; } = null!;
    [JsonPropertyName("version")]         public string Version         { get; init; } = null!;
    [JsonPropertyName("entryAssembly")]   public string EntryAssembly  { get; init; } = null!;
    [JsonPropertyName("entryType")]       public string EntryType      { get; init; } = null!;
    [JsonPropertyName("author")]          public string Author         { get; init; } = string.Empty;
    [JsonPropertyName("description")]     public string Description    { get; init; } = string.Empty;
    [JsonPropertyName("sdkVersion")]      public string SdkVersion     { get; init; } = "1.0";
    [JsonPropertyName("apiVersion")]      public string ApiVersion     { get; init; } = "1";
    [JsonPropertyName("startupOrder")]    public int    StartupOrder   { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]  public string MinHostVersion { get; init; } = "1.0.0";
    [JsonPropertyName("maxHostVersion")]  public string MaxHostVersion { get; init; } = "999.999.999";
    [JsonPropertyName("permissions")]     public IReadOnlyList<string> Permissions  { get; init; } = [];
    [JsonPropertyName("dependencies")]    public IReadOnlyList<string> Dependencies { get; init; } = [];
    [JsonPropertyName("capabilities")]    public IReadOnlyList<string> Capabilities { get; init; } = [];
}

// MSOSync.Cli/Http/MsoSyncHttpClient.cs — two constructors for testability
public sealed class MsoSyncHttpClient : IDisposable
{
    // Production: builds HttpClient from baseUrl + bearerToken
    public MsoSyncHttpClient(string baseUrl, string? bearerToken = null);
    // Test: accepts pre-built HttpClient
    public MsoSyncHttpClient(HttpClient httpClient);
}
```

---

## Naming Conversion Algorithm (`plugin new`)

Split `pluginId` on `.` and `-`, PascalCase each segment, join with `.` for namespace and assembly name, take last segment + `Plugin` for the class name.

| Input | Assembly / Namespace | Class |
|-------|----------------------|-------|
| `acme.myrouter` | `Acme.MyRouter` | `MyRouterPlugin` |
| `company.sql-collector` | `Company.SqlCollector` | `SqlCollectorPlugin` |
| `org.a.b.plugin` | `Org.A.B.Plugin` | `PluginPlugin` |
