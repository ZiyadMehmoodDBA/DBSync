# Phase 2C.4 — Task 2: `plugin new` + `plugin pack` Commands

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.

**Goal:** Implement `msosync plugin new <name>` (scaffolds a plugin project from embedded templates) and `msosync plugin pack` (compiles via `dotnet publish`, zips to `.msopkg`). Includes `CliPluginManifest`, `PluginScaffolder`, `PluginPacker`, `PackageSigningService`, all four template files, and their unit tests.

**Depends on:** Task 1 (project structure, `CliConsole`, `MsoSyncHttpClient` all exist)
**Produces:** `PluginNewCommand`, `PluginPackCommand` (consumed by Task 4 `Program.cs` wiring)

## Global Constraints (from master plan)

- `CliPluginManifest` is the CLI's own manifest record — it does NOT reference `MSOSync.Plugin`
- Templates are embedded as `EmbeddedResource` in `MSOSync.Cli.csproj`
- `dotnet publish` is invoked as a child `Process` — no MSBuild API
- `dotnet sn -R` is invoked as a child `Process` for signing (skipped silently if no key)
- `.msopkg` is a standard ZIP archive (uses `System.IO.Compression.ZipFile`)
- Exit code 2 for manifest/validation failures; exit code 1 for build/file failures
- Plugin ID must match `^[a-z][a-z0-9]*(\.[a-z][a-z0-9-]*)*$`

## Files Created

**`src/MSOSync.Cli/`**
- `Packaging/CliPluginManifest.cs`
- `Packaging/PluginPacker.cs`
- `Packaging/PackageSigningService.cs`
- `Scaffolding/PluginScaffolder.cs`
- `Scaffolding/Templates/Plugin.csproj.template`
- `Scaffolding/Templates/PluginImpl.cs.template`
- `Scaffolding/Templates/plugin.json.template`
- `Scaffolding/Templates/plugin.config.json.template`
- `Commands/PluginNewCommand.cs`
- `Commands/PluginPackCommand.cs`

**`tests/MSOSync.CliTests/`**
- `Commands/PluginNewCommandTests.cs`
- `Commands/PluginPackCommandTests.cs`
- `Packaging/PluginPackerTests.cs`

**`src/MSOSync.Cli/MSOSync.Cli.csproj`** — add `EmbeddedResource` for templates

---

- [ ] **Step 1: Create `src/MSOSync.Cli/Packaging/CliPluginManifest.cs`**

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Cli.Packaging;

/// <summary>
/// CLI-local copy of the plugin manifest schema. Avoids a reference to MSOSync.Plugin.
/// Must stay in sync with MSOSync.Plugin.Models.PluginManifest JSON field names.
/// </summary>
public sealed record CliPluginManifest
{
    [JsonPropertyName("manifestVersion")] public int    ManifestVersion { get; init; } = 1;
    [JsonPropertyName("id")]              public string Id              { get; init; } = null!;
    [JsonPropertyName("name")]            public string Name            { get; init; } = null!;
    [JsonPropertyName("version")]         public string Version         { get; init; } = null!;
    [JsonPropertyName("sdkVersion")]      public string SdkVersion      { get; init; } = "1.0";
    [JsonPropertyName("apiVersion")]      public string ApiVersion      { get; init; } = "1";
    [JsonPropertyName("startupOrder")]    public int    StartupOrder    { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]  public string MinHostVersion  { get; init; } = "1.0.0";
    [JsonPropertyName("maxHostVersion")]  public string MaxHostVersion  { get; init; } = "999.999.999";
    [JsonPropertyName("entryAssembly")]   public string EntryAssembly   { get; init; } = null!;
    [JsonPropertyName("entryType")]       public string EntryType       { get; init; } = null!;
    [JsonPropertyName("author")]          public string Author          { get; init; } = string.Empty;
    [JsonPropertyName("description")]     public string Description     { get; init; } = string.Empty;
    [JsonPropertyName("permissions")]     public IReadOnlyList<string>  Permissions  { get; init; } = [];
    [JsonPropertyName("dependencies")]    public IReadOnlyList<string>  Dependencies { get; init; } = [];
    [JsonPropertyName("capabilities")]    public IReadOnlyList<string>  Capabilities { get; init; } = [];
}
```

- [ ] **Step 2: Create `src/MSOSync.Cli/Scaffolding/Templates/Plugin.csproj.template`**

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

- [ ] **Step 3: Create `src/MSOSync.Cli/Scaffolding/Templates/PluginImpl.cs.template`**

Use `{{Namespace}}`, `{{ClassName}}` as substitution tokens.

```
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Hosting;

namespace {{Namespace}};

// Entry point declared in plugin.json → entryType
public sealed class {{ClassName}} : PluginBase
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

- [ ] **Step 4: Create `src/MSOSync.Cli/Scaffolding/Templates/plugin.json.template`**

Use `{{Id}}`, `{{Name}}`, `{{AssemblyName}}`, `{{Namespace}}`, `{{ClassName}}`, `{{Author}}`, `{{Description}}` as substitution tokens.

```json
{
  "manifestVersion": 1,
  "id":             "{{Id}}",
  "name":           "{{Name}}",
  "version":        "1.0.0",
  "sdkVersion":     "1.0",
  "apiVersion":     "1",
  "startupOrder":   1000,
  "minHostVersion": "1.0.0",
  "maxHostVersion": "999.999.999",
  "entryAssembly":  "{{AssemblyName}}.dll",
  "entryType":      "{{Namespace}}.{{ClassName}}",
  "author":         "{{Author}}",
  "description":    "{{Description}}",
  "permissions":    [],
  "dependencies":   [],
  "capabilities":   []
}
```

- [ ] **Step 5: Create `src/MSOSync.Cli/Scaffolding/Templates/plugin.config.json.template`**

```json
{
  "settings": {}
}
```

- [ ] **Step 6: Add `EmbeddedResource` entries to `src/MSOSync.Cli/MSOSync.Cli.csproj`**

Add a new `<ItemGroup>` inside the project file:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Scaffolding\Templates\Plugin.csproj.template" />
    <EmbeddedResource Include="Scaffolding\Templates\PluginImpl.cs.template" />
    <EmbeddedResource Include="Scaffolding\Templates\plugin.json.template" />
    <EmbeddedResource Include="Scaffolding\Templates\plugin.config.json.template" />
  </ItemGroup>
```

- [ ] **Step 7: Create `src/MSOSync.Cli/Scaffolding/PluginScaffolder.cs`**

```csharp
using System.Reflection;
using System.Text.RegularExpressions;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Scaffolding;

public static class PluginScaffolder
{
    private static readonly Regex IdPattern =
        new(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9-]*)*$", RegexOptions.Compiled);

    /// <summary>Validates the plugin ID format.</summary>
    public static bool IsValidId(string id) => IdPattern.IsMatch(id);

    /// <summary>
    /// Derives assembly name and class name from a plugin ID.
    /// e.g. "acme.my-router" → ("Acme.MyRouter", "MyRouterPlugin")
    /// </summary>
    public static (string AssemblyName, string ClassName) DeriveNames(string pluginId)
    {
        // Split on '.' first to get segments, then split each segment on '-'
        string[] segments = pluginId
            .Split('.')
            .SelectMany(seg => seg.Split('-'))
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..])
            .ToArray();

        string assemblyName = string.Join(".", pluginId
            .Split('.')
            .Select(dotSeg => string.Join(string.Empty,
                dotSeg.Split('-').Select(s => char.ToUpperInvariant(s[0]) + s[1..]))));

        string className = segments[^1] + "Plugin";

        return (assemblyName, className);
    }

    /// <summary>
    /// Scaffolds a new plugin project directory. Returns 0 on success, 1 or 2 on failure.
    /// </summary>
    public static int Scaffold(string pluginId, string outputDir, string author, string description)
    {
        if (!IsValidId(pluginId))
        {
            CliConsole.Error($"Plugin ID must match pattern: ^[a-z][a-z0-9]*(\\.[a-z][a-z0-9-]*)*$");
            CliConsole.Error($"Got: \"{pluginId}\"");
            return 2;
        }

        if (Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            CliConsole.Error($"Target directory already exists and is non-empty: {outputDir}");
            return 1;
        }

        (string assemblyName, string className) = DeriveNames(pluginId);
        // Last dot-segment is the display name portion
        string[] dotParts  = pluginId.Split('.');
        string   nameParts = dotParts[^1];
        string   displayName = string.Join(" ", nameParts.Split('-')
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

        Directory.CreateDirectory(outputDir);

        var tokens = new Dictionary<string, string>
        {
            ["{{Id}}"]           = pluginId,
            ["{{Name}}"]         = displayName,
            ["{{AssemblyName}}"] = assemblyName,
            ["{{Namespace}}"]    = assemblyName,
            ["{{ClassName}}"]    = className,
            ["{{Author}}"]       = author,
            ["{{Description}}"]  = description
        };

        // Map: (embedded resource suffix → output file name)
        var templateMap = new[]
        {
            ("Plugin.csproj.template",          $"{assemblyName}.csproj"),
            ("PluginImpl.cs.template",           $"{className}.cs"),
            ("plugin.json.template",             "plugin.json"),
            ("plugin.config.json.template",      "plugin.config.json")
        };

        foreach ((string resourceSuffix, string outputFile) in templateMap)
        {
            string content = ReadTemplate(resourceSuffix);
            foreach ((string token, string value) in tokens)
                content = content.Replace(token, value);

            File.WriteAllText(Path.Combine(outputDir, outputFile), content);
        }

        CliConsole.Ok($"Created plugin project: {outputDir}/");
        CliConsole.Info($"     {outputDir}/{assemblyName}.csproj");
        CliConsole.Info($"     {outputDir}/{className}.cs");
        CliConsole.Info($"     {outputDir}/plugin.json");
        CliConsole.Info($"     {outputDir}/plugin.config.json");
        CliConsole.Info(string.Empty);
        CliConsole.Info("Next steps:");
        CliConsole.Info($"  cd {outputDir}");
        CliConsole.Info("  dotnet build");
        CliConsole.Info("  msosync plugin pack");

        return 0;
    }

    private static string ReadTemplate(string resourceSuffix)
    {
        Assembly asm  = typeof(PluginScaffolder).Assembly;
        // Resource names use namespace-style dots: MSOSync.Cli.Scaffolding.Templates.<suffix-with-dots-replaced-by-dots>
        // The embedded resource name mirrors the folder path using '.' separators
        string   name = asm.GetManifestResourceNames()
                           .Single(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        using Stream stream = asm.GetManifestResourceStream(name)!;
        using var   reader  = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 8: Create `src/MSOSync.Cli/Packaging/PackageSigningService.cs`**

```csharp
using System.Diagnostics;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Packaging;

public static class PackageSigningService
{
    /// <summary>
    /// Signs <paramref name="assemblyPath"/> with <paramref name="keyPath"/> using `dotnet sn -R`.
    /// Returns true on success (or when skipped because keyPath is empty).
    /// Returns false if signing fails.
    /// </summary>
    public static bool TrySign(string assemblyPath, string? keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            CliConsole.Warn("No signing key configured — package is unsigned");
            return true;
        }

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList          = { "sn", "-R", assemblyPath, keyPath },
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using Process proc = Process.Start(psi)!;
        proc.WaitForExit();

        if (proc.ExitCode == 0)
        {
            CliConsole.Ok($"Signed: {Path.GetFileName(assemblyPath)} ({Path.GetFileName(keyPath)})");
            return true;
        }

        string err = proc.StandardError.ReadToEnd();
        CliConsole.Error($"Signing failed: {err.Trim()}");
        return false;
    }
}
```

- [ ] **Step 9: Create `src/MSOSync.Cli/Packaging/PluginPacker.cs`**

```csharp
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using MSOSync.Cli.Output;

namespace MSOSync.Cli.Packaging;

public static class PluginPacker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Full pack pipeline. Returns 0 on success, 1 on build/IO failure, 2 on manifest validation failure.
    /// </summary>
    public static async Task<int> PackAsync(
        string workingDir,
        string outputDir,
        string configuration,
        string? signingKeyPath,
        CancellationToken ct = default)
    {
        // Step 1+2: Locate and parse manifest
        string manifestPath = Path.Combine(workingDir, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            CliConsole.Error("plugin.json not found in current directory");
            return 2;
        }

        CliPluginManifest? manifest;
        try
        {
            string json = await File.ReadAllTextAsync(manifestPath, ct);
            manifest    = JsonSerializer.Deserialize<CliPluginManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            CliConsole.Error($"Failed to parse plugin.json: {ex.Message}");
            return 2;
        }

        if (manifest is null)
        {
            CliConsole.Error("plugin.json deserialized to null");
            return 2;
        }

        // Step 3: Validate required manifest fields
        if (!ValidateManifest(manifest, out string validationError))
        {
            CliConsole.Error(validationError);
            return 2;
        }

        // Step 4: dotnet publish
        string stageDir = Path.Combine(workingDir, "artifacts", ".msopkg-stage");
        if (Directory.Exists(stageDir))
            Directory.Delete(stageDir, recursive: true);

        int buildResult = await RunDotnetPublishAsync(workingDir, configuration, stageDir, ct);
        if (buildResult != 0)
        {
            CliConsole.Error($"dotnet publish exited with code {buildResult}");
            return 1;
        }

        CliConsole.Ok($"Built: {configuration}");

        // Step 5: Verify entry assembly exists
        string entryAssemblyPath = Path.Combine(stageDir, manifest.EntryAssembly);
        if (!File.Exists(entryAssemblyPath))
        {
            CliConsole.Error($"Entry assembly not found after publish: {manifest.EntryAssembly}");
            return 1;
        }

        // Step 6: Optional signing
        if (!PackageSigningService.TrySign(entryAssemblyPath, signingKeyPath))
            return 1;

        // Step 7+8: Zip to .msopkg and write manifest.json inside archive
        Directory.CreateDirectory(outputDir);
        string pkgFileName = $"{manifest.Id}-{manifest.Version}.msopkg";
        string pkgPath     = Path.Combine(outputDir, pkgFileName);

        if (File.Exists(pkgPath))
            File.Delete(pkgPath);

        // Copy plugin.json into stage dir as manifest.json (canonical archive name)
        File.Copy(manifestPath, Path.Combine(stageDir, "manifest.json"), overwrite: true);

        ZipFile.CreateFromDirectory(stageDir, pkgPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        long sizeKb = new FileInfo(pkgPath).Length / 1024;
        CliConsole.Ok($"Packed: {outputDir}/{pkgFileName} ({sizeKb} KB)");

        // Step 9: Clean stage directory
        Directory.Delete(stageDir, recursive: true);

        return 0;
    }

    /// <summary>Validates that required manifest fields are non-null/non-empty.</summary>
    public static bool ValidateManifest(CliPluginManifest manifest, out string error)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        { error = "plugin.json: 'id' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        { error = "plugin.json: 'name' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        { error = "plugin.json: 'version' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        { error = "plugin.json: 'entryAssembly' is required"; return false; }

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
        { error = "plugin.json: 'entryType' is required"; return false; }

        error = string.Empty;
        return true;
    }

    private static async Task<int> RunDotnetPublishAsync(
        string workingDir, string configuration, string outputPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList           = { "publish", "-c", configuration, "-o", outputPath },
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        using Process proc = Process.Start(psi)!;

        // Forward output to console so the user sees build progress
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);
        return proc.ExitCode;
    }
}
```

- [ ] **Step 10: Create `src/MSOSync.Cli/Commands/PluginNewCommand.cs`**

```csharp
using System.CommandLine;
using MSOSync.Cli.Config;
using MSOSync.Cli.Output;
using MSOSync.Cli.Scaffolding;

namespace MSOSync.Cli.Commands;

public sealed class PluginNewCommand
{
    public Command Build()
    {
        var nameArg   = new Argument<string>("name",
            "Plugin identifier in reverse-DNS format (e.g. acme.myrouter)");
        var outputOpt = new Option<string?>("--output",
            "Target directory to create the project in (default: ./<name>)");
        var authorOpt = new Option<string>("--author",
            () => string.Empty, "Author string written into plugin.json");
        var descOpt   = new Option<string>("--description",
            () => string.Empty, "Description written into plugin.json");

        var cmd = new Command("new", "Scaffold a new plugin project directory");
        cmd.AddArgument(nameArg);
        cmd.AddOption(outputOpt);
        cmd.AddOption(authorOpt);
        cmd.AddOption(descOpt);

        cmd.SetHandler(async (name, output, author, description) =>
        {
            int exitCode = await ExecuteAsync(name, output, author, description);
            Environment.Exit(exitCode);
        }, nameArg, outputOpt, authorOpt, descOpt);

        return cmd;
    }

    /// <summary>Testable entry point. Returns exit code.</summary>
    public Task<int> ExecuteAsync(
        string name,
        string? output,
        string author,
        string description,
        CancellationToken ct = default)
    {
        string targetDir = output ?? Path.Combine(Directory.GetCurrentDirectory(), name);
        int    result    = PluginScaffolder.Scaffold(name, targetDir, author, description);
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 11: Create `src/MSOSync.Cli/Commands/PluginPackCommand.cs`**

```csharp
using System.CommandLine;
using MSOSync.Cli.Config;
using MSOSync.Cli.Packaging;

namespace MSOSync.Cli.Commands;

public sealed class PluginPackCommand
{
    public Command Build()
    {
        var outputOpt = new Option<string>("--output",
            () => "artifacts", "Directory where .msopkg is written");
        var configOpt = new Option<string>("--configuration",
            () => "Release", "MSBuild configuration (Release or Debug)");
        var signKeyOpt = new Option<string?>("--sign-key",
            "Path to .snk key file for strong-name signing");

        var cmd = new Command("pack", "Compile and pack the plugin into a .msopkg archive");
        cmd.AddOption(outputOpt);
        cmd.AddOption(configOpt);
        cmd.AddOption(signKeyOpt);

        cmd.SetHandler(async (output, configuration, signKey) =>
        {
            CliConfig config      = CliConfigStore.Load();
            string?   effectiveKey = signKey ?? (string.IsNullOrEmpty(config.SigningKeyPath)
                ? null : config.SigningKeyPath);

            int exitCode = await ExecuteAsync(
                Directory.GetCurrentDirectory(), output, configuration, effectiveKey);
            Environment.Exit(exitCode);
        }, outputOpt, configOpt, signKeyOpt);

        return cmd;
    }

    /// <summary>Testable entry point. Returns exit code.</summary>
    public Task<int> ExecuteAsync(
        string workingDir,
        string outputDir,
        string configuration,
        string? signingKeyPath,
        CancellationToken ct = default)
        => PluginPacker.PackAsync(workingDir, outputDir, configuration, signingKeyPath, ct);
}
```

- [ ] **Step 12: Create `tests/MSOSync.CliTests/Commands/PluginNewCommandTests.cs`**

```csharp
using MSOSync.Cli.Scaffolding;

namespace MSOSync.CliTests.Commands;

public sealed class PluginNewCommandTests : IDisposable
{
    private readonly string _tempDir;

    public PluginNewCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── Name conversion ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("acme.myrouter",       "Acme.MyRouter",        "MyRouterPlugin")]
    [InlineData("company.sql-router",  "Company.SqlRouter",    "SqlRouterPlugin")]
    [InlineData("org.a.b.plugin",      "Org.A.B.Plugin",       "PluginPlugin")]
    [InlineData("x.y",                 "X.Y",                  "YPlugin")]
    [InlineData("acme.sql-collector",  "Acme.SqlCollector",    "SqlCollectorPlugin")]
    public void DeriveNames_ReturnsCorrectAssemblyAndClass(
        string pluginId, string expectedAssembly, string expectedClass)
    {
        (string assembly, string className) = PluginScaffolder.DeriveNames(pluginId);
        Assert.Equal(expectedAssembly, assembly);
        Assert.Equal(expectedClass, className);
    }

    // ── ID validation ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("acme.myrouter",       true)]
    [InlineData("company.sql-router",  true)]
    [InlineData("x.y.z",              true)]
    [InlineData("a1.b2",              true)]
    [InlineData("",                   false)]
    [InlineData("MyPlugin",           false)]    // uppercase
    [InlineData("acme myrouter",      false)]    // space
    [InlineData(".starts-with-dot",   false)]
    [InlineData("1starts-with-digit", false)]
    public void IsValidId_ReturnsExpected(string id, bool expected)
    {
        Assert.Equal(expected, PluginScaffolder.IsValidId(id));
    }

    // ── Scaffold — success ───────────────────────────────────────────────────

    [Fact]
    public async Task Scaffold_CreatesAllFourFiles_OnValidId()
    {
        string outputDir = Path.Combine(_tempDir, "acme.myrouter");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        int exitCode = await cmd.ExecuteAsync("acme.myrouter", outputDir, "Acme", "My router plugin");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(outputDir, "Acme.MyRouter.csproj")));
        Assert.True(File.Exists(Path.Combine(outputDir, "MyRouterPlugin.cs")));
        Assert.True(File.Exists(Path.Combine(outputDir, "plugin.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "plugin.config.json")));
    }

    [Fact]
    public async Task Scaffold_InjectsPluginIdIntoPluginJson()
    {
        string outputDir = Path.Combine(_tempDir, "org.checker");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        await cmd.ExecuteAsync("org.checker", outputDir, "Org", "Checker plugin");

        string json = await File.ReadAllTextAsync(Path.Combine(outputDir, "plugin.json"));
        Assert.Contains("\"org.checker\"", json);
        Assert.Contains("Org.Checker.dll", json);
        Assert.Contains("Org.Checker.CheckerPlugin", json);
    }

    [Fact]
    public async Task Scaffold_InjectsAuthorAndDescriptionIntoPluginJson()
    {
        string outputDir = Path.Combine(_tempDir, "acme.ext");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        await cmd.ExecuteAsync("acme.ext", outputDir, "Acme Corp", "Extension plugin");

        string json = await File.ReadAllTextAsync(Path.Combine(outputDir, "plugin.json"));
        Assert.Contains("Acme Corp", json);
        Assert.Contains("Extension plugin", json);
    }

    [Fact]
    public async Task Scaffold_InjectsNamespaceIntoCs()
    {
        string outputDir = Path.Combine(_tempDir, "acme.myrouter2");
        var    cmd       = new Cli.Commands.PluginNewCommand();

        await cmd.ExecuteAsync("acme.myrouter2", outputDir, string.Empty, string.Empty);

        string cs = await File.ReadAllTextAsync(Path.Combine(outputDir, "MyRouter2Plugin.cs"));
        Assert.Contains("namespace Acme.MyRouter2;", cs);
        Assert.Contains("class MyRouter2Plugin", cs);
    }

    // ── Scaffold — failure paths ─────────────────────────────────────────────

    [Fact]
    public async Task Scaffold_Returns2_OnInvalidId()
    {
        var cmd = new Cli.Commands.PluginNewCommand();
        int exitCode = await cmd.ExecuteAsync("Invalid.Plugin", Path.Combine(_tempDir, "out"), "", "");
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task Scaffold_Returns1_WhenDirectoryAlreadyExists()
    {
        string outputDir = Path.Combine(_tempDir, "acme.dup");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "existing.txt"), "content");

        var cmd      = new Cli.Commands.PluginNewCommand();
        int exitCode = await cmd.ExecuteAsync("acme.dup", outputDir, "", "");
        Assert.Equal(1, exitCode);
    }
}
```

- [ ] **Step 13: Create `tests/MSOSync.CliTests/Packaging/PluginPackerTests.cs`**

```csharp
using System.IO.Compression;
using System.Text.Json;
using MSOSync.Cli.Packaging;

namespace MSOSync.CliTests.Packaging;

/// <summary>
/// Tests for PluginPacker manifest validation paths.
/// Full pack pipeline (dotnet publish) is not exercised in unit tests —
/// that requires an end-to-end build environment.
/// </summary>
public sealed class PluginPackerTests
{
    // ── ValidateManifest ─────────────────────────────────────────────────────

    [Fact]
    public void ValidateManifest_ReturnsTrue_WhenAllRequiredFieldsPresent()
    {
        var manifest = new CliPluginManifest
        {
            Id            = "acme.test",
            Name          = "Test Plugin",
            Version       = "1.0.0",
            EntryAssembly = "Acme.Test.dll",
            EntryType     = "Acme.Test.TestPlugin"
        };

        bool valid = PluginPacker.ValidateManifest(manifest, out string error);

        Assert.True(valid);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("id",            "")]
    [InlineData("name",          "")]
    [InlineData("version",       "")]
    [InlineData("entryAssembly", "")]
    [InlineData("entryType",     "")]
    public void ValidateManifest_ReturnsFalse_WhenRequiredFieldMissing(string field, string _)
    {
        var manifest = new CliPluginManifest
        {
            Id            = field == "id"            ? string.Empty : "acme.test",
            Name          = field == "name"          ? string.Empty : "Test",
            Version       = field == "version"       ? string.Empty : "1.0.0",
            EntryAssembly = field == "entryAssembly" ? string.Empty : "Acme.Test.dll",
            EntryType     = field == "entryType"     ? string.Empty : "Acme.Test.TestPlugin"
        };

        bool valid = PluginPacker.ValidateManifest(manifest, out string error);

        Assert.False(valid);
        Assert.NotEmpty(error);
    }

    // ── PackAsync — manifest-not-found path ──────────────────────────────────

    [Fact]
    public async Task PackAsync_Returns2_WhenPluginJsonMissing()
    {
        string workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workDir);
        try
        {
            int result = await PluginPacker.PackAsync(workDir, "artifacts", "Release", null);
            Assert.Equal(2, result);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackAsync_Returns2_WhenPluginJsonIsMalformed()
    {
        string workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workDir);
        try
        {
            File.WriteAllText(Path.Combine(workDir, "plugin.json"), "{ not valid }");
            int result = await PluginPacker.PackAsync(workDir, "artifacts", "Release", null);
            Assert.Equal(2, result);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackAsync_Returns2_WhenManifestMissingRequiredField()
    {
        string workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workDir);
        try
        {
            // id is missing → validation should fail
            string json = """{"name":"Test","version":"1.0.0","entryAssembly":"T.dll","entryType":"T"}""";
            File.WriteAllText(Path.Combine(workDir, "plugin.json"), json);
            int result = await PluginPacker.PackAsync(workDir, "artifacts", "Release", null);
            Assert.Equal(2, result);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
```

- [ ] **Step 14: Create `tests/MSOSync.CliTests/Commands/PluginPackCommandTests.cs`**

```csharp
using MSOSync.Cli.Commands;
using MSOSync.Cli.Packaging;

namespace MSOSync.CliTests.Commands;

/// <summary>
/// Tests PluginPackCommand.ExecuteAsync validation paths
/// (delegates to PluginPacker — build pipeline tests live in PluginPackerTests).
/// </summary>
public sealed class PluginPackCommandTests
{
    [Fact]
    public async Task ExecuteAsync_Returns2_WhenNoPluginJson()
    {
        string workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workDir);
        try
        {
            var cmd      = new PluginPackCommand();
            int exitCode = await cmd.ExecuteAsync(workDir, "artifacts", "Release", null);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Returns2_WhenManifestInvalid()
    {
        string workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workDir);
        try
        {
            // version field missing
            File.WriteAllText(Path.Combine(workDir, "plugin.json"),
                """{"id":"acme.test","name":"Test","entryAssembly":"T.dll","entryType":"T"}""");
            var cmd      = new PluginPackCommand();
            int exitCode = await cmd.ExecuteAsync(workDir, "artifacts", "Release", null);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
```

- [ ] **Step 15: Build and run tests**

```powershell
dotnet build src\MSOSync.Cli\MSOSync.Cli.csproj
dotnet test tests\MSOSync.CliTests\MSOSync.CliTests.csproj `
    --filter "FullyQualifiedName~PluginNewCommandTests|FullyQualifiedName~PluginPackerTests|FullyQualifiedName~PluginPackCommandTests"
```

Expected: all tests pass, 0 errors, 0 warnings.

- [ ] **Step 16: Commit**

```powershell
git add src\MSOSync.Cli\ tests\MSOSync.CliTests\
git commit -m "feat(2C.4-T2): plugin new + pack commands — PluginScaffolder, CliPluginManifest, PluginPacker, PackageSigningService, templates, unit tests"
```
