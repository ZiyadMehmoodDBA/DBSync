# Phase 2C.4 — CLI Tooling Design Specification

**Date:** 2026-07-23
**Status:** Approved
**Phase:** 2C — SDK & Ecosystem
**Deliverable:** `msosync` .NET global tool (`MSOSync.Cli` project)

---

## Goal

Deliver a standalone `msosync` .NET global tool that enables plugin developers and operators to scaffold plugin projects, pack and publish `.msopkg` packages, and interact with a running MSOSync server (health checks, plugin install, plugin list). The tool has zero dependency on ASP.NET Core or EF Core and runs as a pure console application.

---

## Architecture

### Project Location

```
src/
└── MSOSync.Cli/
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
    │   └── Console.cs
    ├── Packaging/
    │   ├── PluginPacker.cs
    │   └── PackageSigningService.cs
    └── Scaffolding/
        ├── PluginScaffolder.cs
        └── Templates/
            ├── Plugin.csproj.template
            ├── PluginImpl.cs.template
            ├── plugin.json.template
            └── plugin.config.json.template

tests/
└── MSOSync.CliTests/
    ├── MSOSync.CliTests.csproj
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

### Project File — `MSOSync.Cli.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>msosync</ToolCommandName>
    <PackageId>MSOSync.Cli</PackageId>
    <Version>1.0.0</Version>
    <Description>MSOSync CLI — plugin scaffolding, packaging, publishing, and server management</Description>
    <IsPackable>true</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

Key points:
- `PackAsTool` + `ToolCommandName` make `dotnet tool install -g MSOSync.Cli` register `msosync` on the PATH.
- The only project reference is `MSOSync.Sdk` — for `PluginManifest`, `PluginCapability`, `PluginPermission`.
- `MSOSync.Api`, `MSOSync.Metadata`, `MSOSync.Persistence`, and `MSOSync.Plugin` are never referenced.
- `System.CommandLine` v2 beta4 is the stable-preview that ships with .NET 9 toolchain.

### Dependency Rules

```
MSOSync.Cli
  └── MSOSync.Sdk              (PluginManifest, PluginCapability, PluginPermission only)

No reference to:
  × MSOSync.Api
  × MSOSync.Metadata
  × MSOSync.Persistence
  × MSOSync.Plugin
  × MSOSync.Common
  × MSOSync.App
```

`System.IO.Compression`, `System.Net.Http`, and `System.Text.Json` are inbox BCL — no NuGet entry required.

### Test Project File — `MSOSync.CliTests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Cli\MSOSync.Cli.csproj" />
  </ItemGroup>
</Project>
```

No `Moq` — `HttpClient` is mocked via `FakeHttpMessageHandler` (pattern defined in Testing section).

---

## Command Reference

### Root Command Layout

```
msosync
├── plugin
│   ├── new <name>
│   ├── pack [--output <dir>]
│   ├── publish <file> [--registry <url>] [--api-key <key>]
│   ├── install <id[@version]> [--server <url>] [--token <jwt>]
│   └── list [--server <url>] [--token <jwt>]
└── server
    └── health [--server <url>]
```

---

### Command 1 — `msosync plugin new <name>`

**Purpose:** Scaffold a new plugin project directory from embedded templates.

**Syntax:**
```
msosync plugin new <name> [--output <dir>] [--author <author>] [--description <desc>]
```

**Arguments:**

| Name | Required | Description |
|---|---|---|
| `name` | yes | Plugin identifier in reverse-DNS format (e.g. `acme.myrouter`) |

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--output <dir>` | `./<name>` relative to cwd | Target directory to create the project in |
| `--author <author>` | `""` | Author string written into `plugin.json` |
| `--description <desc>` | `""` | Description written into `plugin.json` |

**What it creates (given `msosync plugin new acme.myrouter`):**

```
acme.myrouter/
├── Acme.MyRouter.csproj
├── MyRouterPlugin.cs
├── plugin.json
└── plugin.config.json
```

**`Acme.MyRouter.csproj` (scaffolded):**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>13.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <!-- Replace VERSION with the MSOSync.Sdk NuGet version you are targeting -->
    <PackageReference Include="MSOSync.Sdk" Version="1.0.0" />
  </ItemGroup>
</Project>
```

**`MyRouterPlugin.cs` (scaffolded):**

```csharp
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace Acme.MyRouter;

// Entry point declared in plugin.json → entryType
public sealed class MyRouterPlugin : PluginBase
{
    public override async Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        // TODO: read configuration via context.Configuration
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // TODO: start background work
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        // TODO: stop background work
        return base.StopAsync(cancellationToken);
    }
}
```

**`plugin.json` (scaffolded):**

```json
{
  "manifestVersion": 1,
  "id":             "acme.myrouter",
  "name":           "My Router",
  "version":        "1.0.0",
  "sdkVersion":     "1.0",
  "apiVersion":     "1",
  "startupOrder":   1000,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "999.999.999",
  "entryAssembly":  "Acme.MyRouter.dll",
  "entryType":      "Acme.MyRouter.MyRouterPlugin",
  "author":         "",
  "description":    "",
  "permissions":    [],
  "dependencies":   [],
  "capabilities":   []
}
```

**`plugin.config.json` (scaffolded):**

```json
{
  "settings": {}
}
```

**Naming convention derivation from `<name>`:**

| Input (`name`) | Assembly name | Namespace | Class name | Directory |
|---|---|---|---|---|
| `acme.myrouter` | `Acme.MyRouter` | `Acme.MyRouter` | `MyRouterPlugin` | `acme.myrouter/` |
| `company.sql-collector` | `Company.SqlCollector` | `Company.SqlCollector` | `SqlCollectorPlugin` | `company.sql-collector/` |

Algorithm: split on `.` and `-`, PascalCase each segment, join with `.` for namespace/assembly, take last segment + `Plugin` for class name.

**Exit codes:**
- `0` — directory and all files created successfully
- `1` — target directory already exists (non-empty)
- `2` — `name` fails format validation (must match `^[a-z][a-z0-9]*(\.[a-z][a-z0-9-]*)*$`)

**Console output (success):**
```
[OK] Created plugin project: acme.myrouter/
     acme.myrouter/Acme.MyRouter.csproj
     acme.myrouter/MyRouterPlugin.cs
     acme.myrouter/plugin.json
     acme.myrouter/plugin.config.json

Next steps:
  cd acme.myrouter
  dotnet build
  msosync plugin pack
```

---

### Command 2 — `msosync plugin pack [--output <dir>]`

**Purpose:** Compile the current plugin project, gather output DLLs, optionally sign, and produce a `.msopkg` archive.

**Syntax:**
```
msosync plugin pack [--output <dir>] [--configuration <Release|Debug>] [--sign-key <path>]
```

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--output <dir>` | `./artifacts/` | Directory where `.msopkg` is written |
| `--configuration <cfg>` | `Release` | MSBuild configuration passed to `dotnet publish` |
| `--sign-key <path>` | loaded from `~/.msosync/config.json` `signingKeyPath` | Path to `.snk` key file for strong-name signing |

**Pack pipeline (sequential steps):**

1. **Locate manifest** — find `plugin.json` in the current working directory. Exit 2 if not found.
2. **Parse manifest** — deserialize `plugin.json` into `PluginManifest`. Exit 2 on parse failure.
3. **Validate manifest fields** — `id`, `name`, `version`, `entryAssembly`, `entryType` must be non-null/non-empty. Exit 2 on validation failure.
4. **Build/publish** — invoke `dotnet publish -c {configuration} -o ./artifacts/.msopkg-stage/` as a child process. Exit 1 if `dotnet publish` returns non-zero.
5. **Verify entry assembly** — confirm `./artifacts/.msopkg-stage/{entryAssembly}` exists. Exit 1 if missing.
6. **Sign (optional)** — if a signing key is configured, invoke `dotnet sn -R {assembly} {keyFile}` on the entry assembly. Skip silently if no key configured.
7. **Zip to `.msopkg`** — create a ZIP archive at `{output}/{id}-{version}.msopkg` containing the entire staging directory contents. `.msopkg` is a ZIP file with a `.msopkg` extension.
8. **Write manifest sidecar** — copy `plugin.json` into the archive root as `manifest.json` (canonical name for marketplace validation).
9. **Clean stage directory** — delete `./artifacts/.msopkg-stage/`.

**Output archive layout:**

```
{id}-{version}.msopkg   (ZIP)
├── manifest.json        ← copy of plugin.json with canonical name
├── {EntryAssembly}.dll  ← compiled entry DLL
├── *.dll                ← all other compiled DLLs
└── lib/                 ← any subdirectories from publish output
    └── *.dll
```

**Exit codes:**
- `0` — `.msopkg` file created successfully
- `1` — build failed or entry assembly missing
- `2` — manifest not found or manifest validation failed

**Console output (success):**
```
[OK] Built: Release
[OK] Signed: Acme.MyRouter.dll (acme-signing.snk)
[OK] Packed: artifacts/acme.myrouter-1.0.0.msopkg (142 KB)
```

**Console output (no signing key):**
```
[OK] Built: Release
[WRN] No signing key configured — package is unsigned
[OK] Packed: artifacts/acme.myrouter-1.0.0.msopkg (142 KB)
```

---

### Command 3 — `msosync plugin publish <file.msopkg> [--registry <url>] [--api-key <key>]`

**Purpose:** Upload a `.msopkg` file to a marketplace registry.

**Syntax:**
```
msosync plugin publish <file> [--registry <url>] [--api-key <key>]
```

**Arguments:**

| Name | Required | Description |
|---|---|---|
| `file` | yes | Path to the `.msopkg` file to publish |

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--registry <url>` | `config.json` → `registryUrl` | Base URL of the marketplace registry |
| `--api-key <key>` | `config.json` → `registryApiKey` | API key for registry authentication |

**HTTP call:**

```
POST {registryUrl}/api/v1/packages
Content-Type: multipart/form-data
Authorization: ApiKey {apiKey}

Body: multipart form with field name "package", file content from <file>
```

**Response handling:**

| HTTP status | Behavior |
|---|---|
| `201 Created` | Print success, exit 0 |
| `400 Bad Request` | Print server error body, exit 2 |
| `401 Unauthorized` | Print "Authentication failed — check --api-key", exit 1 |
| `409 Conflict` | Print "Version already exists on registry", exit 1 |
| `5xx` | Print "Registry server error: {status}", exit 1 |

**Exit codes:**
- `0` — published successfully
- `1` — network or server error
- `2` — validation failure (bad package or auth)

**Console output (success):**
```
[OK] Publishing acme.myrouter-1.0.0.msopkg → https://marketplace.msosync.io
[OK] Published: acme.myrouter@1.0.0
     Registry: https://marketplace.msosync.io
     Install:  msosync plugin install acme.myrouter@1.0.0
```

---

### Command 4 — `msosync plugin install <id[@version]> [--server <url>] [--token <jwt>]`

**Purpose:** Install a plugin on a running MSOSync server by calling the server's Marketplace API.

**Syntax:**
```
msosync plugin install <id[@version]> [--server <url>] [--token <jwt>]
```

**Arguments:**

| Name | Required | Description |
|---|---|---|
| `id[@version]` | yes | Plugin ID, optionally with `@version` suffix (e.g. `acme.myrouter@1.2.0`). Omitting version installs the latest available. |

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--server <url>` | `config.json` → `serverUrl` | Base URL of the MSOSync server |
| `--token <jwt>` | `config.json` → `serverToken` | JWT bearer token for server authentication |

**ID/version parsing:**

```
"acme.myrouter"        → id = "acme.myrouter",  version = null  (latest)
"acme.myrouter@1.2.0"  → id = "acme.myrouter",  version = "1.2.0"
```

**HTTP call:**

```
POST {serverUrl}/api/v1/marketplace/plugins/install
Content-Type: application/json
Authorization: Bearer {token}

{
  "pluginId": "acme.myrouter",
  "version":  "1.2.0"          // null = latest
}
```

**Response handling:**

| HTTP status | Behavior |
|---|---|
| `200 OK` | Print install summary from response body, exit 0 |
| `202 Accepted` | Print "Install queued — check status with: msosync plugin list", exit 0 |
| `400 Bad Request` | Print server error body, exit 2 |
| `401 Unauthorized` | Print "Authentication failed — check --token", exit 1 |
| `404 Not Found` | Print "Plugin not found: {id}@{version}", exit 2 |
| `409 Conflict` | Print "Plugin already installed", exit 1 |
| `5xx` | Print "Server error: {status}", exit 1 |

**Exit codes:**
- `0` — install accepted or completed
- `1` — error (auth, server error, conflict)
- `2` — not found or validation failure

**Console output (success):**
```
[OK] Installing acme.myrouter@1.2.0 on https://server.example.com
[OK] Installed: acme.myrouter@1.2.0
     Status: Running
```

---

### Command 5 — `msosync plugin list [--server <url>] [--token <jwt>]`

**Purpose:** List all plugins currently installed on a running MSOSync server.

**Syntax:**
```
msosync plugin list [--server <url>] [--token <jwt>]
```

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--server <url>` | `config.json` → `serverUrl` | Base URL of the MSOSync server |
| `--token <jwt>` | `config.json` → `serverToken` | JWT bearer token |

**HTTP call:**

```
GET {serverUrl}/api/v1/plugins
Authorization: Bearer {token}
```

**Expected response body:**

```json
[
  {
    "id":          "acme.myrouter",
    "name":        "My Router",
    "version":     "1.2.0",
    "status":      "Running",
    "author":      "Acme Corp",
    "description": "Custom routing plugin"
  }
]
```

**Response handling:**

| HTTP status | Behavior |
|---|---|
| `200 OK` | Render table, exit 0 |
| `401 Unauthorized` | Print "Authentication failed — check --token", exit 1 |
| `5xx` | Print "Server error: {status}", exit 1 |

**Console output (success, tabular format):**
```
ID                    NAME          VERSION  STATUS   AUTHOR
acme.myrouter         My Router     1.2.0    Running  Acme Corp
msosync.sqlserver     SQL Collector 2.1.0    Running  MSOSync
legacy.oldplugin      Old Plugin    0.9.0    Stopped  Unknown

3 plugin(s) installed on https://server.example.com
```

**Exit codes:**
- `0` — list returned (including empty list)
- `1` — auth error or server error

---

### Command 6 — `msosync server health [--server <url>]`

**Purpose:** Check the health of a running MSOSync server.

**Syntax:**
```
msosync server health [--server <url>]
```

**Options:**

| Flag | Default | Description |
|---|---|---|
| `--server <url>` | `config.json` → `serverUrl` | Base URL of the MSOSync server |

**HTTP call:**

```
GET {serverUrl}/health
```

No `Authorization` header — health endpoint is public.

**Response handling:**

The response is the standard ASP.NET Core health check JSON:

```json
{
  "status": "Healthy",
  "results": {
    "database": { "status": "Healthy" },
    "plugins":  { "status": "Healthy" }
  }
}
```

| HTTP status | `status` value | Behavior |
|---|---|---|
| `200` | `Healthy` | Print status table, exit 0 |
| `200` | `Degraded` | Print status table with warnings, exit 0 |
| `503` | `Unhealthy` | Print status table with errors, exit 1 |
| Connection refused | — | Print "Cannot reach server at {url}", exit 1 |
| Non-200/503 | — | Print "Unexpected response: {status}", exit 1 |

**Console output (healthy):**
```
[OK] Server: https://server.example.com
     Status:   Healthy
     database: Healthy
     plugins:  Healthy
```

**Console output (degraded):**
```
[WRN] Server: https://server.example.com
      Status:   Degraded
      database: Healthy
      plugins:  Degraded — 1 plugin in Failed state
```

**Console output (unhealthy):**
```
[ERR] Server: https://server.example.com
      Status:   Unhealthy
      database: Unhealthy — connection timeout
      plugins:  Healthy
```

**Exit codes:**
- `0` — server is Healthy or Degraded
- `1` — server is Unhealthy, connection refused, or unexpected response

---

## Config File Schema

**File:** `~/.msosync/config.json`

Created automatically on first run if it does not exist. All fields are optional with documented defaults.

```json
{
  "serverUrl":       "http://localhost:5000",
  "serverToken":     "",
  "registryUrl":     "https://marketplace.msosync.io",
  "registryApiKey":  "",
  "signingKeyPath":  ""
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `serverUrl` | string | `http://localhost:5000` | Default MSOSync server base URL (used when `--server` is not specified) |
| `serverToken` | string | `""` | Default JWT bearer token for server API calls |
| `registryUrl` | string | `https://marketplace.msosync.io` | Default marketplace registry base URL |
| `registryApiKey` | string | `""` | Default API key for registry publish |
| `signingKeyPath` | string | `""` | Path to `.snk` signing key; empty = unsigned |

**`CliConfig` record:**

```csharp
namespace MSOSync.Cli.Config;

public sealed record CliConfig
{
    public string ServerUrl      { get; init; } = "http://localhost:5000";
    public string ServerToken    { get; init; } = string.Empty;
    public string RegistryUrl    { get; init; } = "https://marketplace.msosync.io";
    public string RegistryApiKey { get; init; } = string.Empty;
    public string SigningKeyPath { get; init; } = string.Empty;
}
```

**`CliConfigStore` contract:**

```csharp
namespace MSOSync.Cli.Config;

public sealed class CliConfigStore
{
    // Returns path: {Environment.GetFolderPath(SpecialFolder.UserProfile)}/.msosync/config.json
    public static string ConfigPath { get; }

    // Load config from disk. Returns CliConfig.defaults if file does not exist.
    public static CliConfig Load();

    // Save config to disk. Creates directory if needed.
    public static void Save(CliConfig config);
}
```

**Precedence (highest to lowest):**
1. CLI flag (`--server`, `--token`, `--registry`, `--api-key`, `--sign-key`)
2. `~/.msosync/config.json`
3. Hardcoded defaults in `CliConfig`

---

## HTTP Client Patterns

The CLI uses a single `MsoSyncHttpClient` wrapper class. No DI container. `HttpClient` instances are created per command invocation with a 30-second timeout.

### `MsoSyncHttpClient`

```csharp
namespace MSOSync.Cli.Http;

public sealed class MsoSyncHttpClient : IDisposable
{
    private readonly HttpClient _http;

    public MsoSyncHttpClient(string baseUrl, string? bearerToken = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrEmpty(bearerToken))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
    }

    // GET {path} → deserialize response as T
    public Task<T?> GetAsync<T>(string path, CancellationToken ct = default);

    // POST {path} with JSON body → returns HttpResponseMessage
    public Task<HttpResponseMessage> PostJsonAsync<T>(string path, T body, CancellationToken ct = default);

    // POST {path} with multipart form (file upload) → returns HttpResponseMessage
    public Task<HttpResponseMessage> PostMultipartAsync(string path, string fieldName,
        string filePath, CancellationToken ct = default);

    // GET {path} with ApiKey header (registry auth)
    public Task<HttpResponseMessage> GetWithApiKeyAsync(string path, string apiKey, CancellationToken ct = default);

    public void Dispose() => _http.Dispose();
}
```

### HttpClient lifecycle

Each command method constructs and disposes `MsoSyncHttpClient` within a `using` block. This avoids socket exhaustion in the CLI context because commands are short-lived one-shot invocations, not long-running services.

### Serialization

All JSON uses `System.Text.Json` with `JsonSerializerOptions`:

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
};
```

### Timeout and cancellation

All HTTP methods accept a `CancellationToken`. The 30-second `HttpClient.Timeout` is the outer hard limit. Commands do not implement their own retry logic.

---

## Output / Console Formatting

**File:** `src/MSOSync.Cli/Output/Console.cs`

```csharp
namespace MSOSync.Cli.Output;

public static class CliConsole
{
    public static void Ok(string message);      // [OK]  in green
    public static void Warn(string message);    // [WRN] in yellow
    public static void Error(string message);   // [ERR] in red
    public static void Info(string message);    // plain white
    public static void Table(string[] headers, IEnumerable<string[]> rows);
}
```

**Color conventions:**

| Prefix | Console color | Meaning |
|---|---|---|
| `[OK]` | `ConsoleColor.Green` | Success |
| `[WRN]` | `ConsoleColor.Yellow` | Warning (partial success or degraded) |
| `[ERR]` | `ConsoleColor.Red` | Error |
| *(none)* | `ConsoleColor.White` | Informational |

`CliConsole` always writes to `Console.Out` for `[OK]`/`[WRN]`/info and to `Console.Error` for `[ERR]`. This allows callers to capture stdout and inspect it in scripts.

**Table rendering:** left-aligned columns padded to max column width + 2 spaces. Header row followed by a separator of dashes.

---

## Error Handling

### Exit codes summary

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Operational error (network error, auth failure, server error, build failure) |
| `2` | Validation failure (bad input, not found, already exists, manifest parse failure) |

### Exception handling policy

All commands catch exceptions at the top level and map them to exit codes:

```csharp
catch (HttpRequestException ex)
{
    CliConsole.Error($"Network error: {ex.Message}");
    return 1;
}
catch (TaskCanceledException)
{
    CliConsole.Error("Request timed out after 30 seconds");
    return 1;
}
catch (JsonException ex)
{
    CliConsole.Error($"Response parse error: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    CliConsole.Error($"Unexpected error: {ex.Message}");
    return 1;
}
```

No stack traces are printed to the user. Stack traces go to `Console.Error` only when the environment variable `MSOSYNC_CLI_DEBUG=1` is set.

### Validation failure path

Input validation (manifest checks, format checks, file existence checks) returns exit code 2. Validation failures always print a specific error message before the generic error line:

```
[ERR] Plugin ID must match pattern: ^[a-z][a-z0-9]*(\.[a-z][a-z0-9-]*)*$
      Got: "My.Plugin Name"
```

---

## Testing Approach

### Test project structure

All tests are in `MSOSync.CliTests`. Tests cover command logic with mocked HTTP. No integration tests that call a real server.

### FakeHttpMessageHandler pattern

```csharp
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_handler(request));
}
```

Usage in tests:

```csharp
var handler = new FakeHttpMessageHandler(req =>
    new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""[{"id":"acme.plugin","status":"Running"}]""",
                                    Encoding.UTF8, "application/json")
    });

var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
var client = new MsoSyncHttpClient(http); // overload accepting pre-built HttpClient
```

`MsoSyncHttpClient` exposes a constructor overload that accepts an `HttpClient` directly for testability.

### Test coverage targets

| Area | Target |
|---|---|
| `PluginNewCommand` — name parsing, file generation | 100% of name conversion logic |
| `PluginPackCommand` — manifest validation, archive structure | All validation paths + archive content |
| `PluginPublishCommand` — HTTP status → exit code mapping | All response codes |
| `PluginInstallCommand` — id@version parsing, HTTP status mapping | All parse variants, all response codes |
| `PluginListCommand` — table rendering | Empty list, single plugin, multiple plugins |
| `ServerHealthCommand` — status value mapping | Healthy, Degraded, Unhealthy, connection refused |
| `CliConfigStore` — load/save/defaults | File missing, file present, malformed file |

### Sample test — `PluginListCommandTests`

```csharp
[Fact]
public async Task ListCommand_Returns0_WhenServerReturnsPlugins()
{
    // Arrange
    var responseJson = """
        [{"id":"acme.router","name":"My Router","version":"1.0.0","status":"Running","author":"Acme"}]
        """;
    var client = BuildHttpClient(HttpStatusCode.OK, responseJson);
    var command = new PluginListCommand(client);

    // Act
    int exitCode = await command.ExecuteAsync("http://localhost:5000", token: null);

    // Assert
    Assert.Equal(0, exitCode);
}

[Fact]
public async Task ListCommand_Returns1_WhenServerReturns401()
{
    var client = BuildHttpClient(HttpStatusCode.Unauthorized, "");
    var command = new PluginListCommand(client);
    int exitCode = await command.ExecuteAsync("http://localhost:5000", token: "bad");
    Assert.Equal(1, exitCode);
}
```

### Sample test — `PluginNewCommandTests`

```csharp
[Theory]
[InlineData("acme.myrouter",       "Acme.MyRouter",        "MyRouterPlugin")]
[InlineData("company.sql-router",  "Company.SqlRouter",    "SqlRouterPlugin")]
[InlineData("org.a.b.plugin",      "Org.A.B.Plugin",       "PluginPlugin")]
public void NameConversion_ProducesCorrectAssemblyAndClass(
    string pluginId, string expectedAssembly, string expectedClass)
{
    var (assembly, className) = PluginScaffolder.DeriveNames(pluginId);
    Assert.Equal(expectedAssembly, assembly);
    Assert.Equal(expectedClass, className);
}
```

---

## Global Constraints

1. **No DI container** — `Microsoft.Extensions.DependencyInjection` is not referenced. Dependencies are constructed manually in each command's `ExecuteAsync`.

2. **No ASP.NET Core** — `Microsoft.AspNetCore.*` packages are not referenced. The tool is a pure console executable.

3. **No EF Core** — `Microsoft.EntityFrameworkCore.*` packages are not referenced.

4. **MSOSync.Sdk is the only project reference** — types imported: `PluginManifest` (re-exported from `MSOSync.Plugin.Models` — see note), `PluginCapability`, `PluginPermission`. If `PluginManifest` lives only in `MSOSync.Plugin` at implementation time, the CLI defines its own local `PluginManifest` record matching the JSON schema exactly and does not add a `MSOSync.Plugin` reference.

5. **`plugin.json` is the canonical file name on disk** — `manifest.json` is the canonical name inside the `.msopkg` archive (used by registry validation). Both names represent the same schema.

6. **`.msopkg` is a standard ZIP file** — implementors can open it with any ZIP tool. The file extension alone distinguishes it as an MSOSync plugin package.

7. **Unsigned packages are permitted** — signing is optional. The server decides whether to accept unsigned packages based on its own policy.

8. **`dotnet publish` is invoked as a child process** — the CLI does not shell out to MSBuild directly. `dotnet publish` is the public API for producing a self-contained output directory.

9. **Config file is not created until explicitly needed** — `CliConfigStore.Load()` returns defaults if `~/.msosync/config.json` does not exist. `Save()` is only called when the user invokes a config-writing command (not yet in scope for 2C.4 — no `msosync config set` command).

10. **All output to stdout except errors** — `[ERR]` messages go to `Console.Error`. `[OK]`, `[WRN]`, tables, and info text go to `Console.Out`. This supports shell piping: `msosync plugin list | grep Running`.

11. **`--version` flag** — automatically provided by `System.CommandLine` via `RootCommand.SetHandler`. Prints the `PackageVersion` from assembly info.

12. **`--help` flag** — automatically provided by `System.CommandLine` for all commands and subcommands.

13. **C# 13 / .NET 9 / `LangVersion 13.0`** — all code follows the `Directory.Build.props` baseline: `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`.

14. **No interactive prompts** — all required values come from arguments, options, or `config.json`. The tool never calls `Console.ReadLine()`.

---

## Program.cs Structure

```csharp
using System.CommandLine;
using MSOSync.Cli.Commands;

var rootCommand = new RootCommand("MSOSync CLI — plugin scaffolding, packaging, and server management");

var pluginCommand = new Command("plugin", "Manage MSOSync plugins");
pluginCommand.AddCommand(new PluginNewCommand().Build());
pluginCommand.AddCommand(new PluginPackCommand().Build());
pluginCommand.AddCommand(new PluginPublishCommand().Build());
pluginCommand.AddCommand(new PluginInstallCommand().Build());
pluginCommand.AddCommand(new PluginListCommand().Build());
rootCommand.AddCommand(pluginCommand);

var serverCommand = new Command("server", "Interact with a running MSOSync server");
serverCommand.AddCommand(new ServerHealthCommand().Build());
rootCommand.AddCommand(serverCommand);

return await rootCommand.InvokeAsync(args);
```

Each `*Command` class exposes a `Build()` method that constructs and returns a `System.CommandLine.Command` with its arguments, options, and handler wired up. The `ExecuteAsync(...)` method contains the actual logic and is the entry point for unit tests.

---

## Solution Integration

After implementation, the following entries are added to `MSOSync.sln`:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MSOSync.Cli",
    "src\MSOSync.Cli\MSOSync.Cli.csproj", "{<new-guid>}"
EndProject

Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MSOSync.CliTests",
    "tests\MSOSync.CliTests\MSOSync.CliTests.csproj", "{<new-guid>}"
EndProject
```

Both projects are placed under the existing `src` and `tests` solution folders respectively.

The tool is installed locally for development via:

```
dotnet pack src/MSOSync.Cli/MSOSync.Cli.csproj -c Release -o ./artifacts
dotnet tool install --global --add-source ./artifacts MSOSync.Cli
```

Or via the NuGet feed once published:

```
dotnet tool install --global MSOSync.Cli
```
