# Epic 14A — Task 2: PluginManifest + PluginManifestValidator

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement `PluginManifest` deserialization model, `PluginLogEvents` static class (EventId constants), and `PluginManifestValidator` (static class). Create `MSOSync.PluginTests` project and unit tests covering all validation rules.

**Architecture:** All files in `MSOSync.Plugin`. Validator is a static class (no DI). Tests use xUnit + FluentAssertions.

**Tech Stack:** C# 13 / .NET 9 / System.Text.Json / xUnit + FluentAssertions

## Global Constraints

- `MSOSync.Plugin` references only `MSOSync.Common`; `MSOSync.PluginTests` references `MSOSync.Plugin`
- `PluginManifestValidator.Validate` returns `string? error` (null = valid) — no exceptions
- `entryAssembly` path-traversal guard: reject any value containing `/` or `\` or `..`
- All package versions from `Directory.Packages.props`

## Files

**Create:**
- `src/MSOSync.Plugin/Models/PluginManifest.cs`
- `src/MSOSync.Plugin/Loading/PluginLogEvents.cs`
- `src/MSOSync.Plugin/Loading/PluginManifestValidator.cs`
- `tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj`
- `tests/MSOSync.PluginTests/Loading/PluginManifestValidatorTests.cs`

## Interfaces

**Consumes:** `PluginStatus` (from Task 1)

**Produces:**
- `PluginManifest` (consumed by Tasks 3, 4, 5)
- `PluginManifestValidator.Validate(manifest, pluginDirectory, seenIds)` → `string?` (consumed by Task 4)
- `PluginLogEvents` static class (consumed by Tasks 4, 5)

---

- [ ] **Step 1: Create `src/MSOSync.Plugin/Models/PluginManifest.cs`**

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Models;

public sealed class PluginManifest
{
    [JsonPropertyName("id")]             public string Id              { get; init; } = null!;
    [JsonPropertyName("name")]           public string Name            { get; init; } = null!;
    [JsonPropertyName("version")]        public string Version         { get; init; } = null!;
    [JsonPropertyName("minHostVersion")] public string MinHostVersion  { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")] public string MaxHostVersion  { get; init; } = null!;
    [JsonPropertyName("entryAssembly")]  public string EntryAssembly   { get; init; } = null!;
    [JsonPropertyName("entryType")]      public string EntryType       { get; init; } = null!;
    [JsonPropertyName("author")]         public string Author          { get; init; } = null!;
    [JsonPropertyName("description")]    public string Description     { get; init; } = null!;
    [JsonPropertyName("permissions")]    public IReadOnlyList<string> Permissions   { get; init; } = [];
    [JsonPropertyName("dependencies")]   public IReadOnlyList<string> Dependencies  { get; init; } = [];
    [JsonPropertyName("capabilities")]   public IReadOnlyList<string> Capabilities  { get; init; } = [];
}
```

- [ ] **Step 2: Create `src/MSOSync.Plugin/Loading/PluginLogEvents.cs`**

```csharp
using Microsoft.Extensions.Logging;

namespace MSOSync.Plugin.Loading;

public static class PluginLogEvents
{
    public static readonly EventId PluginDirectoryDiscovered = new(1001, "PluginDirectoryDiscovered");
    public static readonly EventId PluginLoaded              = new(1002, "PluginLoaded");
    public static readonly EventId PluginFailed              = new(1003, "PluginFailed");
    public static readonly EventId PluginDisabled            = new(1004, "PluginDisabled");
    public static readonly EventId PluginStartupSummary      = new(1005, "PluginStartupSummary");
}
```

- [ ] **Step 3: Create `src/MSOSync.Plugin/Loading/PluginManifestValidator.cs`**

```csharp
namespace MSOSync.Plugin.Loading;

public static class PluginManifestValidator
{
    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <summary>
    /// Validates a parsed manifest. Returns null on success, or an error message on failure.
    /// </summary>
    /// <param name="manifest">Parsed manifest (may be null if JSON was malformed).</param>
    /// <param name="pluginDirectory">Absolute path to the plugin directory.</param>
    /// <param name="seenIds">Set of plugin IDs already registered this startup (for duplicate detection).</param>
    public static string? Validate(
        Models.PluginManifest? manifest,
        string pluginDirectory,
        IReadOnlySet<string> seenIds)
    {
        if (manifest == null) return "Manifest is null after deserialization.";

        if (string.IsNullOrWhiteSpace(manifest.Id))           return "Field 'id' is required.";
        if (string.IsNullOrWhiteSpace(manifest.Name))         return "Field 'name' is required.";
        if (string.IsNullOrWhiteSpace(manifest.Version))      return "Field 'version' is required.";
        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion)) return "Field 'minHostVersion' is required.";
        if (string.IsNullOrWhiteSpace(manifest.MaxHostVersion)) return "Field 'maxHostVersion' is required.";
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)) return "Field 'entryAssembly' is required.";
        if (string.IsNullOrWhiteSpace(manifest.EntryType))    return "Field 'entryType' is required.";
        if (string.IsNullOrWhiteSpace(manifest.Author))       return "Field 'author' is required.";
        if (string.IsNullOrWhiteSpace(manifest.Description))  return "Field 'description' is required.";

        // Duplicate ID check
        if (seenIds.Contains(manifest.Id))
            return $"Duplicate plugin ID '{manifest.Id}'. First occurrence wins; this one is rejected.";

        // Version must be parseable as System.Version
        if (!Version.TryParse(manifest.Version, out _))
            return $"Field 'version' value '{manifest.Version}' is not a valid semantic version (major.minor.patch).";

        // Path traversal guard on entryAssembly
        if (manifest.EntryAssembly.IndexOfAny(PathSeparators) >= 0 ||
            manifest.EntryAssembly.Contains(".."))
            return $"Field 'entryAssembly' must be a filename only, not a path: '{manifest.EntryAssembly}'.";

        // entryAssembly file must exist in the plugin directory
        var dllPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
        if (!File.Exists(dllPath))
            return $"Entry assembly '{manifest.EntryAssembly}' not found in '{pluginDirectory}'.";

        // No duplicate permissions
        if (manifest.Permissions.Count != manifest.Permissions.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Field 'permissions' contains duplicate values.";

        // No duplicate dependencies
        if (manifest.Dependencies.Count != manifest.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Field 'dependencies' contains duplicate values.";

        return null;
    }
}
```

- [ ] **Step 4: Create `tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Plugin\MSOSync.Plugin.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Add MSOSync.PluginTests to solution**

```bash
dotnet sln D:\MSOSync\MSOSync.sln add tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj
```

- [ ] **Step 6: Write failing tests — `tests/MSOSync.PluginTests/Loading/PluginManifestValidatorTests.cs`**

```csharp
using FluentAssertions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.PluginTests.Loading;

public sealed class PluginManifestValidatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _dll;

    public PluginManifestValidatorTests()
    {
        Directory.CreateDirectory(_dir);
        _dll = Path.Combine(_dir, "Test.dll");
        File.WriteAllBytes(_dll, []);  // empty file — just needs to exist
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static PluginManifest Valid(string dll = "Test.dll") => new()
    {
        Id = "test.plugin", Name = "Test", Version = "1.0.0",
        MinHostVersion = "1.0.0", MaxHostVersion = "99.0.0",
        EntryAssembly = dll, EntryType = "Test.Plugin",
        Author = "Test", Description = "A test plugin",
    };

    [Fact]
    public void Validate_ValidManifest_ReturnsNull()
    {
        var result = PluginManifestValidator.Validate(Valid(), _dir, new HashSet<string>());
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("version")]
    [InlineData("minHostVersion")]
    [InlineData("maxHostVersion")]
    [InlineData("entryAssembly")]
    [InlineData("entryType")]
    [InlineData("author")]
    [InlineData("description")]
    public void Validate_MissingRequiredField_ReturnsError(string field)
    {
        var m = field switch
        {
            "id"             => Valid() with { Id = "" },
            "name"           => Valid() with { Name = "" },
            "version"        => Valid() with { Version = "" },
            "minHostVersion" => Valid() with { MinHostVersion = "" },
            "maxHostVersion" => Valid() with { MaxHostVersion = "" },
            "entryAssembly"  => Valid() with { EntryAssembly = "" },
            "entryType"      => Valid() with { EntryType = "" },
            "author"         => Valid() with { Author = "" },
            "description"    => Valid() with { Description = "" },
            _                => throw new ArgumentException(field),
        };

        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().NotBeNull();
    }

    [Fact]
    public void Validate_BadSemver_ReturnsError()
    {
        var m = Valid() with { Version = "not-a-version" };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("version");
    }

    [Theory]
    [InlineData("sub/dir/Test.dll")]
    [InlineData("..\\Test.dll")]
    [InlineData("../Test.dll")]
    public void Validate_PathTraversalInEntryAssembly_ReturnsError(string badPath)
    {
        var m = Valid() with { EntryAssembly = badPath };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("path");
    }

    [Fact]
    public void Validate_DuplicateId_ReturnsError()
    {
        var m = Valid();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test.plugin" };
        var result = PluginManifestValidator.Validate(m, _dir, seen);
        result.Should().Contain("Duplicate");
    }

    [Fact]
    public void Validate_MissingDll_ReturnsError()
    {
        var m = Valid() with { EntryAssembly = "Missing.dll" };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("not found");
    }

    [Fact]
    public void Validate_DuplicatePermissions_ReturnsError()
    {
        var m = Valid() with { Permissions = ["READ", "READ"] };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("permissions");
    }

    [Fact]
    public void Validate_DuplicateDependencies_ReturnsError()
    {
        var m = Valid() with { Dependencies = ["plugin.a", "plugin.a"] };
        var result = PluginManifestValidator.Validate(m, _dir, new HashSet<string>());
        result.Should().Contain("dependencies");
    }

    [Fact]
    public void Validate_NullManifest_ReturnsError()
    {
        var result = PluginManifestValidator.Validate(null, _dir, new HashSet<string>());
        result.Should().NotBeNull();
    }
}
```

- [ ] **Step 7: Run tests to verify they fail (implementation not wired yet — validator exists but test assertions confirm logic)**

```bash
dotnet test tests/MSOSync.PluginTests --filter "PluginManifestValidatorTests" -v minimal
```

Expected: All tests PASS (validator was implemented in Step 3).

- [ ] **Step 8: Build**

```bash
dotnet build tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/MSOSync.Plugin/Models/PluginManifest.cs src/MSOSync.Plugin/Loading/PluginLogEvents.cs src/MSOSync.Plugin/Loading/PluginManifestValidator.cs tests/MSOSync.PluginTests/ MSOSync.sln
git commit -m "feat(14A-2): PluginManifest, PluginManifestValidator, PluginLogEvents, PluginTests project"
```
