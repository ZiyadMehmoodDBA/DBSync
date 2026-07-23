# Phase 2C.4 — Task 4: `server health` + `Program.cs` + Solution Build + Smoke Test

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.

**Goal:** Implement `ServerHealthCommand`, replace the stub `Program.cs` with full `System.CommandLine` wiring, verify the solution builds clean, run all unit tests, and perform a `dotnet tool install` smoke test confirming `msosync --version` executes.

**Depends on:** Tasks 1, 2, 3 (all commands exist)
**Produces:** Fully functional `msosync` global tool

## Global Constraints (from master plan)

- `server health` exit codes: 0 for Healthy/Degraded, 1 for Unhealthy/connection-refused/unexpected
- `ServerHealthCommand` treats HTTP 200 + `status == "Unhealthy"` as exit 1
- `ServerHealthCommand` treats HTTP 503 as exit 1 regardless of body
- Connection refused (`HttpRequestException`) → print "Cannot reach server at {url}", exit 1
- `Program.cs` must register all six command handlers and return the `System.CommandLine` exit code
- The stub `Program.cs` from Task 1 is completely replaced
- `dotnet pack` + `dotnet tool install --global --add-source` is the smoke-test install path
- `--version` flag is provided automatically by `RootCommand`; `--help` is provided for all commands

## Files Created / Modified

**`src/MSOSync.Cli/Commands/ServerHealthCommand.cs`** — new
**`src/MSOSync.Cli/Program.cs`** — replace stub with full wiring
**`tests/MSOSync.CliTests/Commands/ServerHealthCommandTests.cs`** — new

---

- [ ] **Step 1: Create `src/MSOSync.Cli/Commands/ServerHealthCommand.cs`**

```csharp
using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class ServerHealthCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Command Build()
    {
        var serverOpt = new Option<string?>("--server", "Base URL of the MSOSync server");

        var cmd = new Command("health", "Check the health of a running MSOSync server");
        cmd.AddOption(serverOpt);

        cmd.SetHandler(async (server) =>
        {
            CliConfig config       = CliConfigStore.Load();
            string effectiveServer = server ?? config.ServerUrl;

            int exitCode = await ExecuteAsync(effectiveServer);
            Environment.Exit(exitCode);
        }, serverOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string            serverUrl,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(serverUrl);

        try
        {
            using HttpResponseMessage response =
                await client.GetRawAsync("/health", ct);

            int statusCode = (int)response.StatusCode;

            if (statusCode != 200 && statusCode != 503)
            {
                CliConsole.Error($"Unexpected response: {statusCode}");
                return 1;
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            HealthResponse? health;
            try
            {
                health = JsonSerializer.Deserialize<HealthResponse>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                CliConsole.Error($"Response parse error: {ex.Message}");
                return 1;
            }

            if (health is null)
            {
                CliConsole.Error("Empty or null health response");
                return 1;
            }

            return RenderHealth(serverUrl, health, statusCode);
        }
        catch (HttpRequestException)
        {
            CliConsole.Error($"Cannot reach server at {serverUrl}");
            return 1;
        }
        catch (TaskCanceledException)
        {
            CliConsole.Error("Request timed out after 30 seconds");
            return 1;
        }
        catch (Exception ex)
        {
            CliConsole.Error($"Unexpected error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (ownsClient) client.Dispose();
        }
    }

    private static int RenderHealth(string serverUrl, HealthResponse health, int httpStatus)
    {
        string overallStatus = health.Status ?? "Unknown";

        switch (overallStatus.ToUpperInvariant())
        {
            case "HEALTHY":
                CliConsole.Ok($"Server: {serverUrl}");
                CliConsole.Info($"     Status:   {overallStatus}");
                RenderChecks(health.Results);
                return 0;

            case "DEGRADED":
                CliConsole.Warn($"Server: {serverUrl}");
                CliConsole.Info($"      Status:   {overallStatus}");
                RenderChecks(health.Results);
                return 0;

            case "UNHEALTHY":
            default:
                CliConsole.Error($"Server: {serverUrl}");
                CliConsole.Error($"      Status:   {overallStatus}");
                RenderChecks(health.Results);
                return 1;
        }
    }

    private static void RenderChecks(Dictionary<string, HealthCheckEntry>? results)
    {
        if (results is null) return;
        foreach ((string name, HealthCheckEntry entry) in results)
        {
            string line = string.IsNullOrWhiteSpace(entry.Description)
                ? $"     {name}: {entry.Status}"
                : $"     {name}: {entry.Status} — {entry.Description}";
            CliConsole.Info(line);
        }
    }

    // ── DTOs (internal — not part of public API) ─────────────────────────────

    private sealed record HealthResponse
    {
        [JsonPropertyName("status")]  public string?                               Status  { get; init; }
        [JsonPropertyName("results")] public Dictionary<string, HealthCheckEntry>? Results { get; init; }
    }

    private sealed record HealthCheckEntry
    {
        [JsonPropertyName("status")]      public string? Status      { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
    }
}
```

- [ ] **Step 2: Replace `src/MSOSync.Cli/Program.cs`** (replace the stub entirely)

```csharp
using System.CommandLine;
using MSOSync.Cli.Commands;

// Root command
var rootCommand = new RootCommand("MSOSync CLI — plugin scaffolding, packaging, and server management");

// plugin sub-tree
var pluginCommand = new Command("plugin", "Manage MSOSync plugins");
pluginCommand.AddCommand(new PluginNewCommand().Build());
pluginCommand.AddCommand(new PluginPackCommand().Build());
pluginCommand.AddCommand(new PluginPublishCommand().Build());
pluginCommand.AddCommand(new PluginInstallCommand().Build());
pluginCommand.AddCommand(new PluginListCommand().Build());
rootCommand.AddCommand(pluginCommand);

// server sub-tree
var serverCommand = new Command("server", "Interact with a running MSOSync server");
serverCommand.AddCommand(new ServerHealthCommand().Build());
rootCommand.AddCommand(serverCommand);

return await rootCommand.InvokeAsync(args);
```

- [ ] **Step 3: Create `tests/MSOSync.CliTests/Commands/ServerHealthCommandTests.cs`**

```csharp
using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;

namespace MSOSync.CliTests.Commands;

public sealed class ServerHealthCommandTests
{
    private static MsoSyncHttpClient BuildClient(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        return new MsoSyncHttpClient(http);
    }

    // ── Healthy ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenStatusIsHealthy()
    {
        string body = """
            {
              "status": "Healthy",
              "results": {
                "database": { "status": "Healthy" },
                "plugins":  { "status": "Healthy" }
              }
            }
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(0, exitCode);
    }

    // ── Degraded ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenStatusIsDegraded()
    {
        string body = """
            {
              "status": "Degraded",
              "results": {
                "database": { "status": "Healthy" },
                "plugins":  { "status": "Degraded", "description": "1 plugin in Failed state" }
              }
            }
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(0, exitCode);
    }

    // ── Unhealthy ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenStatusIsUnhealthy()
    {
        string body = """
            {
              "status": "Unhealthy",
              "results": {
                "database": { "status": "Unhealthy", "description": "connection timeout" },
                "plugins":  { "status": "Healthy" }
              }
            }
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.ServiceUnavailable, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_When200ButStatusIsUnhealthy()
    {
        // HTTP 200 but body status = Unhealthy → exit 1
        string body = """{"status": "Unhealthy", "results": {}}""";
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    // ── Connection refused ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenConnectionRefused()
    {
        // FakeHttpMessageHandler that throws HttpRequestException
        var handler = new FakeHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection refused"));
        var http    = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9999") };
        using MsoSyncHttpClient client = new(http);

        var cmd = new ServerHealthCommand();
        int exitCode = await cmd.ExecuteAsync("http://localhost:9999", client);

        Assert.Equal(1, exitCode);
    }

    // ── Unexpected HTTP status ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenNon200Non503Received()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Found, string.Empty);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    // ── Malformed JSON ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenResponseJsonIsMalformed()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, "{ not valid json }");
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(1, exitCode);
    }

    // ── Case-insensitive status matching ─────────────────────────────────────

    [Theory]
    [InlineData("healthy",   0)]
    [InlineData("HEALTHY",   0)]
    [InlineData("Healthy",   0)]
    [InlineData("degraded",  0)]
    [InlineData("DEGRADED",  0)]
    [InlineData("unhealthy", 1)]
    [InlineData("UNHEALTHY", 1)]
    public async Task ExecuteAsync_HandlesStatusCaseInsensitively(string status, int expectedExitCode)
    {
        string body = $$"""{"status": "{{status}}", "results": {}}""";
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, body);
        var cmd = new ServerHealthCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", client);

        Assert.Equal(expectedExitCode, exitCode);
    }
}
```

- [ ] **Step 4: Build the full solution to confirm zero errors**

```powershell
dotnet build MSOSync.sln
```

Expected: `Build succeeded.` 0 errors, 0 warnings across all projects (the `MSOSync.Frontend` esproj may be skipped — that is acceptable).

If `MSOSync.Frontend` causes a build failure unrelated to this task, build just the .NET projects:

```powershell
dotnet build MSOSync.sln --no-restore --exclude "src\MSOSync.Frontend"
```

- [ ] **Step 5: Run all `MSOSync.CliTests` tests**

```powershell
dotnet test tests\MSOSync.CliTests\MSOSync.CliTests.csproj -v normal
```

Expected: all tests pass (no skipped, no failures). Green test count:
- `CliConfigStoreTests` — 5
- `PluginNewCommandTests` — ~9
- `PluginPackerTests` — 5
- `PluginPackCommandTests` — 2
- `PluginPublishCommandTests` — 6
- `PluginInstallCommandTests` — 9
- `PluginListCommandTests` — 5
- `ServerHealthCommandTests` — 9

Total: ~50 tests.

If any test fails:
1. Read the failure message carefully.
2. Check that `FakeHttpMessageHandler` in the test matches the method the command calls (`GetRawAsync` vs `GetAsync`, `PostJsonAsync`, `PostMultipartAsync`).
3. Fix the command or test — do not skip.

- [ ] **Step 6: Pack the CLI tool**

```powershell
dotnet pack src\MSOSync.Cli\MSOSync.Cli.csproj -c Release -o artifacts\cli
```

Expected output ends with:
```
Successfully created package 'artifacts\cli\MSOSync.Cli.1.0.0.nupkg'.
```

- [ ] **Step 7: Install the tool globally (smoke test)**

```powershell
dotnet tool install --global --add-source .\artifacts\cli MSOSync.Cli
```

If a previous version is installed, uninstall first:

```powershell
dotnet tool uninstall --global MSOSync.Cli
dotnet tool install --global --add-source .\artifacts\cli MSOSync.Cli
```

- [ ] **Step 8: Verify `msosync --version` executes**

```powershell
msosync --version
```

Expected output: `1.0.0`

- [ ] **Step 9: Verify `msosync --help` shows all commands**

```powershell
msosync --help
```

Expected output contains:
```
Commands:
  plugin  Manage MSOSync plugins
  server  Interact with a running MSOSync server
```

- [ ] **Step 10: Verify `msosync plugin --help` shows subcommands**

```powershell
msosync plugin --help
```

Expected output contains:
```
Commands:
  new      Scaffold a new plugin project directory
  pack     Compile and pack the plugin into a .msopkg archive
  publish  Upload a .msopkg file to a marketplace registry
  install  Install a plugin on a running MSOSync server
  list     List all plugins installed on a running MSOSync server
```

- [ ] **Step 11: Verify `msosync server health` with no server returns exit 1 (not crash)**

```powershell
msosync server health --server http://localhost:19999
echo "Exit code: $LASTEXITCODE"
```

Expected: prints `[ERR] Cannot reach server at http://localhost:19999` and exits with code 1. No unhandled exception stack trace.

- [ ] **Step 12: Uninstall global tool (keep CI clean)**

```powershell
dotnet tool uninstall --global MSOSync.Cli
```

- [ ] **Step 13: Commit**

```powershell
git add src\MSOSync.Cli\Commands\ServerHealthCommand.cs `
        src\MSOSync.Cli\Program.cs `
        tests\MSOSync.CliTests\Commands\ServerHealthCommandTests.cs
git commit -m "feat(2C.4-T4): server health command, Program.cs wiring, full test suite green, dotnet tool install smoke test"
```

---

## Post-task Checklist

After all four tasks are complete, verify the following:

- [ ] `dotnet build MSOSync.sln` — 0 errors, 0 warnings
- [ ] `dotnet test tests\MSOSync.CliTests\MSOSync.CliTests.csproj` — all tests pass
- [ ] `msosync --version` prints `1.0.0`
- [ ] `msosync plugin new acme.test --output /tmp/acme.test` creates all four files
- [ ] `MSOSync.Cli.csproj` has no reference to `MSOSync.Api`, `MSOSync.Plugin`, `MSOSync.Persistence`, `MSOSync.Common`, or `MSOSync.App`
- [ ] `System.CommandLine` version is pinned in `Directory.Packages.props`, not in the `.csproj`
- [ ] `CliPluginManifest` exists in `MSOSync.Cli.Packaging` — not a reference to `MSOSync.Plugin.Models`
