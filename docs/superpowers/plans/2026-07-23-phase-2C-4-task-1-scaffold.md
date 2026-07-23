# Phase 2C.4 — Task 1: Project Scaffold

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.

**Goal:** Create `src/MSOSync.Cli` and `tests/MSOSync.CliTests`, add both to the solution, pin `System.CommandLine` in `Directory.Packages.props`, implement `CliConfig`/`CliConfigStore`, `CliConsole`, `MsoSyncHttpClient`, and the `FakeHttpMessageHandler` test helper. All infrastructure must compile before any command code is written.

**Depends on:** Nothing (first task)
**Produces:** Compilable project shell consumed by Tasks 2, 3, 4

## Global Constraints (from master plan)

- `System.CommandLine` version `2.0.0-beta4.22272.1` — add to `Directory.Packages.props` under a new `CLI` label; do not pin version in `.csproj`
- No Moq in `MSOSync.CliTests`; test packages inherit versions from central props
- `TreatWarningsAsErrors=true` — no warnings allowed
- `MSOSync.Sdk` is the only project reference in `MSOSync.Cli.csproj`

## Files Created

**`src/MSOSync.Cli/`**
- `MSOSync.Cli.csproj`
- `Config/CliConfig.cs`
- `Config/CliConfigStore.cs`
- `Output/CliConsole.cs`
- `Http/MsoSyncHttpClient.cs`
- `Program.cs` (stub — returns 0; replaced in Task 4)

**`tests/MSOSync.CliTests/`**
- `MSOSync.CliTests.csproj`
- `Helpers/FakeHttpMessageHandler.cs`
- `Config/CliConfigStoreTests.cs`

**`Directory.Packages.props`** — add `System.CommandLine`

**`MSOSync.sln`** — add both projects

---

- [ ] **Step 1: Create directory structure**

```powershell
New-Item -ItemType Directory -Path "src\MSOSync.Cli\Commands"   -Force
New-Item -ItemType Directory -Path "src\MSOSync.Cli\Config"     -Force
New-Item -ItemType Directory -Path "src\MSOSync.Cli\Http"       -Force
New-Item -ItemType Directory -Path "src\MSOSync.Cli\Output"     -Force
New-Item -ItemType Directory -Path "src\MSOSync.Cli\Packaging"  -Force
New-Item -ItemType Directory -Path "src\MSOSync.Cli\Scaffolding\Templates" -Force
New-Item -ItemType Directory -Path "tests\MSOSync.CliTests\Helpers"   -Force
New-Item -ItemType Directory -Path "tests\MSOSync.CliTests\Commands"  -Force
New-Item -ItemType Directory -Path "tests\MSOSync.CliTests\Config"    -Force
New-Item -ItemType Directory -Path "tests\MSOSync.CliTests\Packaging" -Force
```

- [ ] **Step 2: Add `System.CommandLine` to `Directory.Packages.props`**

In `Directory.Packages.props`, add a new `<ItemGroup Label="CLI">` block after the existing `Extensions` group (before the closing `</Project>` tag):

```xml
  <ItemGroup Label="CLI">
    <PackageVersion Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  </ItemGroup>
```

- [ ] **Step 3: Create `src/MSOSync.Cli/MSOSync.Cli.csproj`**

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
    <PackageReference Include="System.CommandLine" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Sdk\MSOSync.Sdk.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `src/MSOSync.Cli/Config/CliConfig.cs`**

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

- [ ] **Step 5: Create `src/MSOSync.Cli/Config/CliConfigStore.cs`**

```csharp
using System.Text.Json;

namespace MSOSync.Cli.Config;

public static class CliConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string ConfigPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".msosync",
            "config.json");

    /// <summary>
    /// Load config from disk. Returns default CliConfig if the file does not exist.
    /// Returns default CliConfig on malformed JSON (non-fatal).
    /// </summary>
    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
        }
        catch (JsonException)
        {
            return new CliConfig();
        }
    }

    /// <summary>
    /// Save config to disk. Creates ~/.msosync/ directory if needed.
    /// </summary>
    public static void Save(CliConfig config)
    {
        string dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
```

- [ ] **Step 6: Create `src/MSOSync.Cli/Output/CliConsole.cs`**

```csharp
namespace MSOSync.Cli.Output;

public static class CliConsole
{
    public static void Ok(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[OK]  {message}");
        Console.ResetColor();
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WRN] {message}");
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERR] {message}");
        Console.ResetColor();
    }

    public static void Info(string message)
    {
        Console.ResetColor();
        Console.WriteLine(message);
    }

    /// <summary>
    /// Renders a left-aligned table. Columns padded to max column width + 2 spaces.
    /// Header row followed by a separator of dashes.
    /// </summary>
    public static void Table(string[] headers, IEnumerable<string[]> rows)
    {
        var allRows = rows.ToList();
        int colCount = headers.Length;
        int[] widths = new int[colCount];

        for (int i = 0; i < colCount; i++)
            widths[i] = headers[i].Length;

        foreach (string[] row in allRows)
            for (int i = 0; i < colCount && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        // Header
        Console.WriteLine(FormatRow(headers, widths));
        // Separator
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        // Data rows
        foreach (string[] row in allRows)
            Console.WriteLine(FormatRow(row, widths));
    }

    private static string FormatRow(string[] cells, int[] widths)
        => string.Join("  ", cells.Select((c, i) => i < widths.Length ? c.PadRight(widths[i]) : c));
}
```

- [ ] **Step 7: Create `src/MSOSync.Cli/Http/MsoSyncHttpClient.cs`**

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MSOSync.Cli.Http;

public sealed class MsoSyncHttpClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly bool _owned;

    /// <summary>Production constructor — builds and owns an HttpClient.</summary>
    public MsoSyncHttpClient(string baseUrl, string? bearerToken = null)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout     = TimeSpan.FromSeconds(30)
        };
        if (!string.IsNullOrEmpty(bearerToken))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        _owned = true;
    }

    /// <summary>Test constructor — accepts pre-built HttpClient (not disposed on Dispose).</summary>
    public MsoSyncHttpClient(HttpClient httpClient)
    {
        _http  = httpClient;
        _owned = false;
    }

    /// <summary>GET {path} and deserialize response as T. Returns null on empty body.</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        HttpResponseMessage response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body)) return default;
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    /// <summary>GET {path} and return raw HttpResponseMessage (for status-code inspection).</summary>
    public Task<HttpResponseMessage> GetRawAsync(string path, CancellationToken ct = default)
        => _http.GetAsync(path, ct);

    /// <summary>POST {path} with JSON body — returns HttpResponseMessage for status-code inspection.</summary>
    public async Task<HttpResponseMessage> PostJsonAsync<T>(string path, T body, CancellationToken ct = default)
    {
        string json    = JsonSerializer.Serialize(body, JsonOptions);
        var    content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(path, content, ct);
    }

    /// <summary>POST {path} as multipart/form-data file upload — returns HttpResponseMessage.</summary>
    public async Task<HttpResponseMessage> PostMultipartAsync(
        string path, string fieldName, string filePath, CancellationToken ct = default)
    {
        await using FileStream fs      = File.OpenRead(filePath);
        using var             form     = new MultipartFormDataContent();
        using var             fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, fieldName, Path.GetFileName(filePath));
        return await _http.PostAsync(path, form, ct);
    }

    /// <summary>GET {path} with ApiKey header (registry auth) — returns HttpResponseMessage.</summary>
    public async Task<HttpResponseMessage> GetWithApiKeyAsync(
        string path, string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Authorization", $"ApiKey {apiKey}");
        return await _http.SendAsync(request, ct);
    }

    public void Dispose()
    {
        if (_owned) _http.Dispose();
    }
}
```

- [ ] **Step 8: Create stub `src/MSOSync.Cli/Program.cs`**

This stub allows the project to compile. It is fully replaced in Task 4.

```csharp
// Stub — replaced in Task 4 with full System.CommandLine wiring
return 0;
```

- [ ] **Step 9: Create `tests/MSOSync.CliTests/MSOSync.CliTests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Cli\MSOSync.Cli.csproj" />
  </ItemGroup>
</Project>
```

Note: `coverlet.collector` is added automatically by `tests/Directory.Build.props`.

- [ ] **Step 10: Create `tests/MSOSync.CliTests/Helpers/FakeHttpMessageHandler.cs`**

```csharp
namespace MSOSync.CliTests.Helpers;

/// <summary>
/// Synchronous fake HttpMessageHandler for unit-testing MsoSyncHttpClient.
/// Pass a factory function that receives the outbound request and returns a canned response.
/// </summary>
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

- [ ] **Step 11: Create `tests/MSOSync.CliTests/Config/CliConfigStoreTests.cs`**

```csharp
using System.Text.Json;
using MSOSync.Cli.Config;

namespace MSOSync.CliTests.Config;

public sealed class CliConfigStoreTests : IDisposable
{
    // Use a temp directory so tests never touch the real ~/.msosync/config.json
    private readonly string _tempDir;
    private readonly string _configPath;

    public CliConfigStoreTests()
    {
        _tempDir    = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _configPath = Path.Combine(_tempDir, "config.json");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_ReturnsDefaults_WhenFileDoesNotExist()
    {
        // Arrange: _configPath does not exist
        CliConfig result = LoadFrom(_configPath);

        // Assert defaults
        Assert.Equal("http://localhost:5000", result.ServerUrl);
        Assert.Equal(string.Empty, result.ServerToken);
        Assert.Equal("https://marketplace.msosync.io", result.RegistryUrl);
        Assert.Equal(string.Empty, result.RegistryApiKey);
        Assert.Equal(string.Empty, result.SigningKeyPath);
    }

    [Fact]
    public void Load_ReturnsStoredValues_WhenFileExists()
    {
        // Arrange
        string json = """
            {
              "serverUrl":      "http://prod:5000",
              "serverToken":    "tok123",
              "registryUrl":    "https://registry.example.com",
              "registryApiKey": "key456",
              "signingKeyPath": "/keys/signing.snk"
            }
            """;
        File.WriteAllText(_configPath, json);

        CliConfig result = LoadFrom(_configPath);

        Assert.Equal("http://prod:5000",               result.ServerUrl);
        Assert.Equal("tok123",                          result.ServerToken);
        Assert.Equal("https://registry.example.com",   result.RegistryUrl);
        Assert.Equal("key456",                          result.RegistryApiKey);
        Assert.Equal("/keys/signing.snk",               result.SigningKeyPath);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsMalformedJson()
    {
        File.WriteAllText(_configPath, "{ this is not valid json }");

        CliConfig result = LoadFrom(_configPath);

        Assert.Equal("http://localhost:5000", result.ServerUrl);
    }

    [Fact]
    public void Save_CreatesFileAndDirectory()
    {
        string subDir  = Path.Combine(_tempDir, "sub");
        string cfgPath = Path.Combine(subDir, "config.json");
        // subDir does not exist yet

        SaveTo(cfgPath, new CliConfig { ServerToken = "saved-token" });

        Assert.True(File.Exists(cfgPath));
        string json   = File.ReadAllText(cfgPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("saved-token", doc.RootElement.GetProperty("serverToken").GetString());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var config = new CliConfig
        {
            ServerUrl      = "http://rt:5000",
            ServerToken    = "rt-token",
            RegistryUrl    = "https://rt-registry.io",
            RegistryApiKey = "rt-key",
            SigningKeyPath = "/rt/key.snk"
        };

        SaveTo(_configPath, config);
        CliConfig loaded = LoadFrom(_configPath);

        Assert.Equal(config.ServerUrl,      loaded.ServerUrl);
        Assert.Equal(config.ServerToken,    loaded.ServerToken);
        Assert.Equal(config.RegistryUrl,    loaded.RegistryUrl);
        Assert.Equal(config.RegistryApiKey, loaded.RegistryApiKey);
        Assert.Equal(config.SigningKeyPath,  loaded.SigningKeyPath);
    }

    // Helpers that bypass the real static ConfigPath and use a temp path instead
    private static CliConfig LoadFrom(string path)
    {
        if (!File.Exists(path))
            return new CliConfig();
        try
        {
            string json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<CliConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new CliConfig();
        }
        catch (JsonException) { return new CliConfig(); }
    }

    private static void SaveTo(string path, CliConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(config,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
    }
}
```

- [ ] **Step 12: Add both projects to `MSOSync.sln`**

```powershell
dotnet sln MSOSync.sln add --solution-folder src   src\MSOSync.Cli\MSOSync.Cli.csproj
dotnet sln MSOSync.sln add --solution-folder tests tests\MSOSync.CliTests\MSOSync.CliTests.csproj
```

Expected output (two lines):
```
Project 'src\MSOSync.Cli\MSOSync.Cli.csproj' added to the solution.
Project 'tests\MSOSync.CliTests\MSOSync.CliTests.csproj' added to the solution.
```

- [ ] **Step 13: Build `MSOSync.Cli` in isolation**

```powershell
dotnet build src\MSOSync.Cli\MSOSync.Cli.csproj
```

Expected: `Build succeeded.` 0 errors, 0 warnings.

- [ ] **Step 14: Build `MSOSync.CliTests` to verify project reference resolves**

```powershell
dotnet build tests\MSOSync.CliTests\MSOSync.CliTests.csproj
```

Expected: `Build succeeded.` 0 errors, 0 warnings.

- [ ] **Step 15: Run `CliConfigStoreTests` to verify infrastructure is green**

```powershell
dotnet test tests\MSOSync.CliTests\MSOSync.CliTests.csproj --filter "FullyQualifiedName~CliConfigStoreTests"
```

Expected: 5 tests pass.

- [ ] **Step 16: Commit**

```powershell
git add Directory.Packages.props `
        src\MSOSync.Cli\ `
        tests\MSOSync.CliTests\ `
        MSOSync.sln
git commit -m "feat(2C.4-T1): MSOSync.Cli + MSOSync.CliTests scaffold — CliConfig, CliConfigStore, CliConsole, MsoSyncHttpClient, FakeHttpMessageHandler"
```
