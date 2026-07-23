# Phase 2C.4 — Task 3: `plugin publish` + `plugin install` + `plugin list` Commands

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.

**Goal:** Implement the three HTTP-based plugin commands (`publish`, `install`, `list`), each with a testable `ExecuteAsync` method, full HTTP-status-to-exit-code mapping, and xUnit tests using `FakeHttpMessageHandler`.

**Depends on:** Task 1 (`MsoSyncHttpClient`, `FakeHttpMessageHandler`, `CliConsole`, `CliConfigStore`)
**Produces:** `PluginPublishCommand`, `PluginInstallCommand`, `PluginListCommand` (consumed by Task 4 `Program.cs`)

## Global Constraints (from master plan)

- All HTTP calls use `MsoSyncHttpClient` inside a `using` block
- Exit codes: 0 success, 1 operational/network/auth error, 2 validation/not-found failure
- `[OK]`/`[WRN]`/info → `Console.Out`; `[ERR]` → `Console.Error`
- `FakeHttpMessageHandler` is used in all tests — no real server calls
- `PluginListCommand` parses a JSON array of `PluginListItem` DTOs and renders a table via `CliConsole.Table`
- `plugin install <id[@version]>`: split on `@`, id left, version right (null if no `@`)

## Files Created

**`src/MSOSync.Cli/Commands/`**
- `PluginPublishCommand.cs`
- `PluginInstallCommand.cs`
- `PluginListCommand.cs`

**`tests/MSOSync.CliTests/Commands/`**
- `PluginPublishCommandTests.cs`
- `PluginInstallCommandTests.cs`
- `PluginListCommandTests.cs`

---

- [ ] **Step 1: Create `src/MSOSync.Cli/Commands/PluginPublishCommand.cs`**

```csharp
using System.CommandLine;
using System.Net;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class PluginPublishCommand
{
    public Command Build()
    {
        var fileArg      = new Argument<string>("file", "Path to the .msopkg file to publish");
        var registryOpt  = new Option<string?>("--registry", "Base URL of the marketplace registry");
        var apiKeyOpt    = new Option<string?>("--api-key",  "API key for registry authentication");

        var cmd = new Command("publish", "Upload a .msopkg file to a marketplace registry");
        cmd.AddArgument(fileArg);
        cmd.AddOption(registryOpt);
        cmd.AddOption(apiKeyOpt);

        cmd.SetHandler(async (file, registry, apiKey) =>
        {
            CliConfig config = CliConfigStore.Load();
            string effectiveRegistry = registry
                ?? (string.IsNullOrEmpty(config.RegistryUrl) ? "https://marketplace.msosync.io" : config.RegistryUrl);
            string effectiveApiKey   = apiKey
                ?? config.RegistryApiKey;

            int exitCode = await ExecuteAsync(file, effectiveRegistry, effectiveApiKey);
            Environment.Exit(exitCode);
        }, fileArg, registryOpt, apiKeyOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string filePath,
        string registryUrl,
        string apiKey,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        if (!File.Exists(filePath))
        {
            CliConsole.Error($"File not found: {filePath}");
            return 2;
        }

        string fileName = Path.GetFileName(filePath);
        CliConsole.Info($"[OK]  Publishing {fileName} → {registryUrl}");

        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(registryUrl);

        try
        {
            using HttpResponseMessage response = await client.PostMultipartAsync(
                "/api/v1/packages", "package", filePath, ct);

            return HandlePublishResponse(response, fileName);
        }
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

    private static int HandlePublishResponse(HttpResponseMessage response, string fileName)
    {
        switch ((int)response.StatusCode)
        {
            case 201:
                // id@version extracted from file name: strip .msopkg, last '-' splits id and version
                string baseName = Path.GetFileNameWithoutExtension(fileName); // e.g. acme.myrouter-1.0.0
                int    dashIdx  = baseName.LastIndexOf('-');
                string idVer    = dashIdx > 0
                    ? $"{baseName[..dashIdx]}@{baseName[(dashIdx + 1)..]}"
                    : baseName;

                CliConsole.Ok($"Published: {idVer}");
                CliConsole.Info($"     Registry: {response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Authority)}");
                CliConsole.Info($"     Install:  msosync plugin install {idVer}");
                return 0;

            case 400:
                string body400 = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                CliConsole.Error(string.IsNullOrWhiteSpace(body400) ? "Bad request" : body400.Trim());
                return 2;

            case 401:
                CliConsole.Error("Authentication failed — check --api-key");
                return 1;

            case 409:
                CliConsole.Error("Version already exists on registry");
                return 1;

            default:
                if ((int)response.StatusCode >= 500)
                {
                    CliConsole.Error($"Registry server error: {(int)response.StatusCode}");
                    return 1;
                }
                CliConsole.Error($"Unexpected response: {(int)response.StatusCode}");
                return 1;
        }
    }
}
```

- [ ] **Step 2: Create `src/MSOSync.Cli/Commands/PluginInstallCommand.cs`**

```csharp
using System.CommandLine;
using System.Net;
using System.Text.Json;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class PluginInstallCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Command Build()
    {
        var idArg      = new Argument<string>("id",
            "Plugin ID, optionally with @version (e.g. acme.myrouter@1.2.0)");
        var serverOpt  = new Option<string?>("--server", "Base URL of the MSOSync server");
        var tokenOpt   = new Option<string?>("--token",  "JWT bearer token for server authentication");

        var cmd = new Command("install", "Install a plugin on a running MSOSync server");
        cmd.AddArgument(idArg);
        cmd.AddOption(serverOpt);
        cmd.AddOption(tokenOpt);

        cmd.SetHandler(async (id, server, token) =>
        {
            CliConfig config        = CliConfigStore.Load();
            string effectiveServer  = server ?? config.ServerUrl;
            string? effectiveToken  = token ?? (string.IsNullOrEmpty(config.ServerToken) ? null : config.ServerToken);

            int exitCode = await ExecuteAsync(id, effectiveServer, effectiveToken);
            Environment.Exit(exitCode);
        }, idArg, serverOpt, tokenOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string            idWithVersion,
        string            serverUrl,
        string?           token,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        (string pluginId, string? version) = ParseIdVersion(idWithVersion);

        CliConsole.Info($"[OK]  Installing {pluginId}{(version is not null ? "@" + version : string.Empty)} on {serverUrl}");

        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(serverUrl, token);

        try
        {
            var body = new { pluginId, version };
            using HttpResponseMessage response =
                await client.PostJsonAsync("/api/v1/marketplace/plugins/install", body, ct);

            return HandleInstallResponse(response, pluginId, version);
        }
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

    /// <summary>
    /// Parses "acme.myrouter@1.2.0" → ("acme.myrouter", "1.2.0")
    /// and  "acme.myrouter"         → ("acme.myrouter", null)
    /// </summary>
    public static (string Id, string? Version) ParseIdVersion(string input)
    {
        int atIndex = input.IndexOf('@');
        if (atIndex < 0)
            return (input, null);
        return (input[..atIndex], input[(atIndex + 1)..]);
    }

    private static int HandleInstallResponse(
        HttpResponseMessage response, string pluginId, string? version)
    {
        string idVer = version is not null ? $"{pluginId}@{version}" : pluginId;

        switch ((int)response.StatusCode)
        {
            case 200:
                string body200 = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string status  = "Installed";
                try
                {
                    using var doc = JsonDocument.Parse(body200);
                    if (doc.RootElement.TryGetProperty("status", out var statusEl))
                        status = statusEl.GetString() ?? status;
                }
                catch { /* ignore parse error — print generic success */ }

                CliConsole.Ok($"Installed: {idVer}");
                CliConsole.Info($"     Status: {status}");
                return 0;

            case 202:
                CliConsole.Ok($"Install queued — check status with: msosync plugin list");
                return 0;

            case 400:
                string body400 = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                CliConsole.Error(string.IsNullOrWhiteSpace(body400) ? "Bad request" : body400.Trim());
                return 2;

            case 401:
                CliConsole.Error("Authentication failed — check --token");
                return 1;

            case 404:
                CliConsole.Error($"Plugin not found: {idVer}");
                return 2;

            case 409:
                CliConsole.Error("Plugin already installed");
                return 1;

            default:
                if ((int)response.StatusCode >= 500)
                {
                    CliConsole.Error($"Server error: {(int)response.StatusCode}");
                    return 1;
                }
                CliConsole.Error($"Unexpected response: {(int)response.StatusCode}");
                return 1;
        }
    }
}
```

- [ ] **Step 3: Create `src/MSOSync.Cli/Commands/PluginListCommand.cs`**

```csharp
using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MSOSync.Cli.Config;
using MSOSync.Cli.Http;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Commands;

public sealed class PluginListCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Command Build()
    {
        var serverOpt = new Option<string?>("--server", "Base URL of the MSOSync server");
        var tokenOpt  = new Option<string?>("--token",  "JWT bearer token");

        var cmd = new Command("list", "List all plugins installed on a running MSOSync server");
        cmd.AddOption(serverOpt);
        cmd.AddOption(tokenOpt);

        cmd.SetHandler(async (server, token) =>
        {
            CliConfig config       = CliConfigStore.Load();
            string effectiveServer = server ?? config.ServerUrl;
            string? effectiveToken = token ?? (string.IsNullOrEmpty(config.ServerToken) ? null : config.ServerToken);

            int exitCode = await ExecuteAsync(effectiveServer, effectiveToken);
            Environment.Exit(exitCode);
        }, serverOpt, tokenOpt);

        return cmd;
    }

    /// <summary>Testable entry point — accepts a pre-built MsoSyncHttpClient for testing.</summary>
    public async Task<int> ExecuteAsync(
        string            serverUrl,
        string?           token,
        MsoSyncHttpClient? httpClient = null,
        CancellationToken  ct = default)
    {
        bool ownsClient = httpClient is null;
        MsoSyncHttpClient client = httpClient
            ?? new MsoSyncHttpClient(serverUrl, token);

        try
        {
            using HttpResponseMessage response =
                await client.GetRawAsync("/api/v1/plugins", ct);

            switch ((int)response.StatusCode)
            {
                case 200:
                    string json = await response.Content.ReadAsStringAsync(ct);
                    var    list = JsonSerializer.Deserialize<List<PluginListItem>>(json, JsonOptions)
                                  ?? [];
                    RenderTable(list, serverUrl);
                    return 0;

                case 401:
                    CliConsole.Error("Authentication failed — check --token");
                    return 1;

                default:
                    if ((int)response.StatusCode >= 500)
                    {
                        CliConsole.Error($"Server error: {(int)response.StatusCode}");
                        return 1;
                    }
                    CliConsole.Error($"Unexpected response: {(int)response.StatusCode}");
                    return 1;
            }
        }
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

    private static void RenderTable(List<PluginListItem> plugins, string serverUrl)
    {
        if (plugins.Count == 0)
        {
            CliConsole.Info("No plugins installed.");
            CliConsole.Info(string.Empty);
            CliConsole.Info($"0 plugin(s) installed on {serverUrl}");
            return;
        }

        string[] headers = ["ID", "NAME", "VERSION", "STATUS", "AUTHOR"];
        IEnumerable<string[]> rows = plugins.Select(p => new[]
        {
            p.Id          ?? string.Empty,
            p.Name        ?? string.Empty,
            p.Version     ?? string.Empty,
            p.Status      ?? string.Empty,
            p.Author      ?? string.Empty
        });

        CliConsole.Table(headers, rows);
        CliConsole.Info(string.Empty);
        CliConsole.Info($"{plugins.Count} plugin(s) installed on {serverUrl}");
    }

    private sealed record PluginListItem
    {
        [JsonPropertyName("id")]          public string? Id          { get; init; }
        [JsonPropertyName("name")]        public string? Name        { get; init; }
        [JsonPropertyName("version")]     public string? Version     { get; init; }
        [JsonPropertyName("status")]      public string? Status      { get; init; }
        [JsonPropertyName("author")]      public string? Author      { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
    }
}
```

- [ ] **Step 4: Create `tests/MSOSync.CliTests/Commands/PluginPublishCommandTests.cs`**

```csharp
using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;

namespace MSOSync.CliTests.Commands;

public sealed class PluginPublishCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pkgPath;

    public PluginPublishCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _pkgPath = Path.Combine(_tempDir, "acme.myrouter-1.0.0.msopkg");
        File.WriteAllBytes(_pkgPath, [0x50, 0x4B]); // minimal fake ZIP header
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static MsoSyncHttpClient BuildClient(HttpStatusCode status, string body = "")
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://marketplace.msosync.io") };
        return new MsoSyncHttpClient(http);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_On201()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Created);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_On400()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.BadRequest, "Version validation failed");
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On401()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Unauthorized);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "bad-key", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On409()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Conflict);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On500()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.InternalServerError);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(_pkgPath, "https://marketplace.msosync.io", "key", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_WhenFileDoesNotExist()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Created);
        var cmd = new PluginPublishCommand();
        int exitCode = await cmd.ExecuteAsync(
            "/non/existent/file.msopkg", "https://marketplace.msosync.io", "key", client);
        Assert.Equal(2, exitCode);
    }
}
```

- [ ] **Step 5: Create `tests/MSOSync.CliTests/Commands/PluginInstallCommandTests.cs`**

```csharp
using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;

namespace MSOSync.CliTests.Commands;

public sealed class PluginInstallCommandTests
{
    private static MsoSyncHttpClient BuildClient(HttpStatusCode status, string body = "")
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        return new MsoSyncHttpClient(http);
    }

    // ── ID / version parsing ─────────────────────────────────────────────────

    [Theory]
    [InlineData("acme.myrouter",       "acme.myrouter", null)]
    [InlineData("acme.myrouter@1.2.0", "acme.myrouter", "1.2.0")]
    [InlineData("acme.myrouter@2.0.0-beta", "acme.myrouter", "2.0.0-beta")]
    public void ParseIdVersion_ReturnsCorrectParts(string input, string expectedId, string? expectedVersion)
    {
        (string id, string? version) = PluginInstallCommand.ParseIdVersion(input);
        Assert.Equal(expectedId,      id);
        Assert.Equal(expectedVersion, version);
    }

    // ── HTTP status mapping ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns0_On200()
    {
        using MsoSyncHttpClient client = BuildClient(
            HttpStatusCode.OK, """{"pluginId":"acme.myrouter","status":"Running"}""");
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter@1.0.0", "http://localhost:5000", null, client);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_On202()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Accepted);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_On400()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.BadRequest, "Invalid plugin ID");
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On401()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Unauthorized);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", "bad", client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_On404()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.NotFound);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.noexist@9.9.9", "http://localhost:5000", null, client);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On409()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Conflict);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_On500()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.InternalServerError);
        var cmd = new PluginInstallCommand();
        int exitCode = await cmd.ExecuteAsync("acme.myrouter", "http://localhost:5000", null, client);
        Assert.Equal(1, exitCode);
    }
}
```

- [ ] **Step 6: Create `tests/MSOSync.CliTests/Commands/PluginListCommandTests.cs`**

```csharp
using System.Net;
using System.Text;
using MSOSync.Cli.Commands;
using MSOSync.Cli.Http;
using MSOSync.CliTests.Helpers;

namespace MSOSync.CliTests.Commands;

public sealed class PluginListCommandTests
{
    private static MsoSyncHttpClient BuildClient(HttpStatusCode status, string body = "")
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        return new MsoSyncHttpClient(http);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenServerReturnsPlugins()
    {
        string responseJson = """
            [
              {"id":"acme.router","name":"My Router","version":"1.0.0","status":"Running","author":"Acme"}
            ]
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, responseJson);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenServerReturnsEmptyList()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, "[]");
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns0_WhenServerReturnsMultiplePlugins()
    {
        string responseJson = """
            [
              {"id":"acme.router",     "name":"My Router",     "version":"1.0.0","status":"Running","author":"Acme"},
              {"id":"msosync.sqlserver","name":"SQL Collector", "version":"2.1.0","status":"Running","author":"MSOSync"},
              {"id":"legacy.plugin",   "name":"Old Plugin",    "version":"0.9.0","status":"Stopped","author":"Unknown"}
            ]
            """;
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.OK, responseJson);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenServerReturns401()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.Unauthorized);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: "bad", client);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Returns1_WhenServerReturns500()
    {
        using MsoSyncHttpClient client = BuildClient(HttpStatusCode.InternalServerError);
        var cmd = new PluginListCommand();

        int exitCode = await cmd.ExecuteAsync("http://localhost:5000", token: null, client);

        Assert.Equal(1, exitCode);
    }
}
```

- [ ] **Step 7: Build and run all Task 3 tests**

```powershell
dotnet build src\MSOSync.Cli\MSOSync.Cli.csproj
dotnet test tests\MSOSync.CliTests\MSOSync.CliTests.csproj `
    --filter "FullyQualifiedName~PluginPublishCommandTests|FullyQualifiedName~PluginInstallCommandTests|FullyQualifiedName~PluginListCommandTests"
```

Expected: all tests pass, 0 errors, 0 warnings.

- [ ] **Step 8: Commit**

```powershell
git add src\MSOSync.Cli\Commands\PluginPublishCommand.cs `
        src\MSOSync.Cli\Commands\PluginInstallCommand.cs `
        src\MSOSync.Cli\Commands\PluginListCommand.cs `
        tests\MSOSync.CliTests\Commands\PluginPublishCommandTests.cs `
        tests\MSOSync.CliTests\Commands\PluginInstallCommandTests.cs `
        tests\MSOSync.CliTests\Commands\PluginListCommandTests.cs
git commit -m "feat(2C.4-T3): plugin publish + install + list commands — HTTP status mapping, FakeHttpMessageHandler tests"
```
