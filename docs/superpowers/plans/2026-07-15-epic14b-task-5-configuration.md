# Epic 14B — Task 5: Plugin Configuration

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement `PluginConfigurationFile` (reads and flattens `plugin.config.json`) and `PluginConfigurationAdapter` (merges appsettings section over file values, implements `IPluginConfiguration`). Write unit tests covering the layering logic and malformed-file handling.

**Architecture:** Config is resolved once at activation time — no runtime reload. Host appsettings (`IConfiguration["Plugins:{pluginId}:*"]`) always wins over bundled `plugin.config.json`. A malformed or oversized config file logs a warning and is treated as empty — the plugin still activates. `PluginConfigurationFile` reads the JSON and flattens it to a colon-separated key hierarchy (same convention as ASP.NET Core configuration).

**Tech Stack:** C# 13 / .NET 9 / `System.Text.Json` / `Microsoft.Extensions.Configuration` / xUnit + FluentAssertions + Moq

## Global Constraints

- `PluginConfigurationFile` and `PluginConfigurationAdapter` are `internal` — not part of public API
- Max config file size controlled by `PluginHostOptions.MaxPluginConfigSizeBytes` (default 1 MB) — passed as a `long` parameter to the loader
- `GetValue<T>` uses `IConfiguration.GetValue<T>` for appsettings (handles type conversion natively); falls back to `Convert.ChangeType` for file values
- `TreatWarningsAsErrors=true`

## Files

**Create:**
- `src/MSOSync.Plugin/Configuration/PluginConfigurationFile.cs`
- `src/MSOSync.Plugin/Configuration/PluginConfigurationAdapter.cs`
- `tests/MSOSync.PluginTests/Configuration/PluginConfigurationTests.cs`

## Interfaces

**Consumes:**
- `IPluginConfiguration` from Task 1

**Produces:**
- `PluginConfigurationFile` — constructor: `static PluginConfigurationFile Load(string configPath, ILogger logger, long maxSizeBytes)`, static `PluginConfigurationFile.Empty`; used by PluginActivator (Task 6)
- `PluginConfigurationAdapter(IConfiguration appSection, PluginConfigurationFile file)` — implements `IPluginConfiguration`; used by PluginActivator (Task 6)

---

- [ ] **Step 1: Write the failing tests first**

Create `tests/MSOSync.PluginTests/Configuration/PluginConfigurationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MSOSync.Plugin.Configuration;
using Xunit;

namespace MSOSync.PluginTests.Configuration;

public sealed class PluginConfigurationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public PluginConfigurationTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteConfig(string json)
    {
        var path = Path.Combine(_tempDir, "plugin.config.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static IConfiguration AppSection(params (string key, string value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.key, p => (string?)p.value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // ── PluginConfigurationFile ─────────────────────────────────────────────

    [Fact]
    public void Load_ValidJson_ReturnsValues()
    {
        var path = WriteConfig("""{"timeout": "30", "nested": {"key": "val"}}""");
        var file = PluginConfigurationFile.Load(path, NullLogger.Instance, 1_048_576);

        file.GetValue("timeout").Should().Be("30");
        file.GetValue("nested:key").Should().Be("val");
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var file = PluginConfigurationFile.Load(
            Path.Combine(_tempDir, "nonexistent.json"),
            NullLogger.Instance, 1_048_576);

        file.Should().BeSameAs(PluginConfigurationFile.Empty);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmpty_NoException()
    {
        var path = WriteConfig("{ this is not json }");
        var file = PluginConfigurationFile.Load(path, NullLogger.Instance, 1_048_576);

        file.Should().BeSameAs(PluginConfigurationFile.Empty);
    }

    [Fact]
    public void Load_ExceedsMaxSize_ReturnsEmpty()
    {
        var path = WriteConfig("""{"key": "value"}""");
        // Max size of 5 bytes — the file is larger
        var file = PluginConfigurationFile.Load(path, NullLogger.Instance, maxSizeBytes: 5);

        file.Should().BeSameAs(PluginConfigurationFile.Empty);
    }

    [Fact]
    public void Load_FlattenedKeys_UseColonSeparator()
    {
        var path = WriteConfig("""{"a": {"b": {"c": "deep"}}}""");
        var file = PluginConfigurationFile.Load(path, NullLogger.Instance, 1_048_576);

        file.GetValue("a:b:c").Should().Be("deep");
    }

    // ── PluginConfigurationAdapter ──────────────────────────────────────────

    [Fact]
    public void GetValue_AppSettingsWinsOverFile()
    {
        var path    = WriteConfig("""{"timeout": "10"}""");
        var file    = PluginConfigurationFile.Load(path, NullLogger.Instance, 1_048_576);
        var adapter = new PluginConfigurationAdapter(AppSection(("timeout", "99")), file);

        adapter.GetValue<int>("timeout").Should().Be(99);
    }

    [Fact]
    public void GetValue_FileUsedWhenNoAppSetting()
    {
        var path    = WriteConfig("""{"timeout": "42"}""");
        var file    = PluginConfigurationFile.Load(path, NullLogger.Instance, 1_048_576);
        var adapter = new PluginConfigurationAdapter(AppSection(), file);

        adapter.GetValue<int>("timeout").Should().Be(42);
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsDefault()
    {
        var adapter = new PluginConfigurationAdapter(AppSection(), PluginConfigurationFile.Empty);

        adapter.GetValue<int>("missing").Should().Be(0);
        adapter.GetValue<string>("missing").Should().BeNull();
    }

    [Fact]
    public void GetValue_WithDefaultValue_ReturnsFallbackWhenMissing()
    {
        var adapter = new PluginConfigurationAdapter(AppSection(), PluginConfigurationFile.Empty);

        adapter.GetValue("missing", 99).Should().Be(99);
    }

    [Fact]
    public void Exists_ReturnsTrueForAppsettingsKey()
    {
        var adapter = new PluginConfigurationAdapter(AppSection(("key", "val")), PluginConfigurationFile.Empty);

        adapter.Exists("key").Should().BeTrue();
        adapter.Exists("other").Should().BeFalse();
    }

    [Fact]
    public void GetSection_SubkeyResolvesRelatively()
    {
        var path    = WriteConfig("""{"db": {"host": "localhost", "port": "5432"}}""");
        var file    = PluginConfigurationFile.Load(path, NullLogger.Instance, 1_048_576);
        var adapter = new PluginConfigurationAdapter(AppSection(), file);

        var section = adapter.GetSection("db");
        section.GetValue<string>("host").Should().Be("localhost");
        section.GetValue<int>("port").Should().Be(5432);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (types don't exist yet)**

```powershell
dotnet test tests\MSOSync.PluginTests --filter "PluginConfigurationTests" -v minimal 2>&1 | Select-Object -First 20
```

Expected: Build error — `PluginConfigurationFile` and `PluginConfigurationAdapter` not found.

- [ ] **Step 3: Create `src/MSOSync.Plugin/Configuration/PluginConfigurationFile.cs`**

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MSOSync.Plugin.Configuration;

internal sealed class PluginConfigurationFile
{
    private readonly IReadOnlyDictionary<string, string?> _values;

    private PluginConfigurationFile(Dictionary<string, string?> values)
        => _values = values;

    public static PluginConfigurationFile Empty { get; } = new([]);

    public string? GetValue(string key)
        => _values.TryGetValue(key, out var v) ? v : null;

    public IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)_values.Keys;

    internal static PluginConfigurationFile FromValues(Dictionary<string, string?> values)
        => new(values);

    public static PluginConfigurationFile Load(string configPath, ILogger logger, long maxSizeBytes)
    {
        if (!File.Exists(configPath))
            return Empty;

        var info = new FileInfo(configPath);
        if (info.Length > maxSizeBytes)
        {
            logger.LogWarning(
                "plugin.config.json at {Path} is {Size} bytes which exceeds the {Max} byte limit; ignoring",
                configPath, info.Length, maxSizeBytes);
            return Empty;
        }

        try
        {
            var json    = File.ReadAllText(configPath);
            var doc     = JsonDocument.Parse(json);
            var dict    = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            FlattenElement(string.Empty, doc.RootElement, dict);
            return new PluginConfigurationFile(dict);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to parse plugin.config.json at {Path}; plugin will use appsettings only",
                configPath);
            return Empty;
        }
    }

    private static void FlattenElement(string prefix, JsonElement element, Dictionary<string, string?> dict)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? prop.Name
                        : $"{prefix}:{prop.Name}";
                    FlattenElement(key, prop.Value, dict);
                }
                break;

            case JsonValueKind.Null:
                dict[prefix] = null;
                break;

            default:
                // Strings, numbers, booleans — strip outer quotes from strings
                dict[prefix] = element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.GetRawText();
                break;
        }
    }
}
```

- [ ] **Step 4: Create `src/MSOSync.Plugin/Configuration/PluginConfigurationAdapter.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using MSOSync.Sdk.Abstractions;

namespace MSOSync.Plugin.Configuration;

internal sealed class PluginConfigurationAdapter(
    IConfiguration          appSection,
    PluginConfigurationFile file) : IPluginConfiguration
{
    public T? GetValue<T>(string key)
    {
        // Priority 1: appsettings section — IConfiguration handles type conversion
        var appStr = appSection[key];
        if (appStr is not null)
            return appSection.GetValue<T>(key);

        // Priority 2: plugin.config.json file
        var fileStr = file.GetValue(key);
        if (fileStr is null)
            return default;

        try
        {
            var underlying = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T?)Convert.ChangeType(fileStr, underlying);
        }
        catch
        {
            return default;
        }
    }

    public T GetValue<T>(string key, T defaultValue)
        => GetValue<T>(key) ?? defaultValue;

    public IPluginConfiguration GetSection(string sectionName)
    {
        var prefix    = sectionName + ":";
        var subValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var k in file.Keys)
        {
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                subValues[k[prefix.Length..]] = file.GetValue(k);
        }

        return new PluginConfigurationAdapter(
            appSection.GetSection(sectionName),
            PluginConfigurationFile.FromValues(subValues));
    }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in appSection.GetChildren())
                keys.Add(child.Key);
            foreach (var k in file.Keys)
                keys.Add(k);
            return keys;
        }
    }

    public bool Exists(string key)
        => appSection[key] is not null || file.GetValue(key) is not null;
}
```

Note: `IConfiguration.GetValue<T>()` is an extension method available from `Microsoft.Extensions.Configuration` (transitively referenced via `Microsoft.Extensions.Options.ConfigurationExtensions`).

- [ ] **Step 5: Run tests**

```powershell
dotnet test tests\MSOSync.PluginTests --filter "PluginConfigurationTests" -v minimal
```

Expected: All 11 tests pass.

- [ ] **Step 6: Build full solution to verify no regressions**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: `Build succeeded.` 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```powershell
git add src\MSOSync.Plugin\Configuration\ tests\MSOSync.PluginTests\Configuration\
git commit -m "feat(14B-5): PluginConfigurationFile + PluginConfigurationAdapter — layered config, malformed-file tolerance"
```
