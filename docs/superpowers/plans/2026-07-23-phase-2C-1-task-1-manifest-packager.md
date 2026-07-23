# Task 1 — ManifestV2 Models, Validator, SDK Constraint Parser, IPluginPackager + PluginPackager

**Phase:** 2C.1
**Depends on:** nothing (parallel-safe with Task 2)
**Produces:** all Packaging models, `ManifestV2Validator`, `SdkVersionConstraintParser`, `PluginPackagingException`, `IPluginPackager`, `PluginPackager`, unit tests for validator + parser + packager

---

## Files to Create

| File | Action |
|------|--------|
| `src/MSOSync.Plugin/Packaging/Models/ManifestV2.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Models/ManifestSignatureBlock.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Models/PackageFileEntry.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Models/PluginDependencyEntry.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Models/PackagingOptions.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Models/PackageInstallResult.cs` | Create |
| `src/MSOSync.Plugin/Packaging/PluginPackagingException.cs` | Create |
| `src/MSOSync.Plugin/Packaging/ManifestV2Validator.cs` | Create |
| `src/MSOSync.Plugin/Packaging/SdkVersionConstraintParser.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Abstractions/IPluginPackager.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Abstractions/IPluginInstaller.cs` | Create (interface only; implementation in Task 3) |
| `src/MSOSync.Plugin/Signing/Abstractions/IPluginSigner.cs` | Create (interface only; implementation in Task 2) |
| `src/MSOSync.Plugin/Packaging/Packager/PluginPackager.cs` | Create |
| `tests/MSOSync.PluginTests/Packaging/ManifestV2ValidatorTests.cs` | Create |
| `tests/MSOSync.PluginTests/Packaging/SdkVersionConstraintParserTests.cs` | Create |
| `tests/MSOSync.PluginTests/Packaging/PluginPackagerTests.cs` | Create |

---

## Interfaces

**Consumes:**
- `MSOSync.Plugin.Models.PluginManifest` (v1, read-only; writes derived v1 manifest during install — done in Task 3)
- `MSOSync.Plugin.Models.PluginHostOptions` (reads `MaxManifestSizeBytes`, `SupportedSdkMajorVersion`, `SupportedApiVersion`)
- `MSOSync.Plugin.Signing.Abstractions.IPluginSigner` (optional; injected into `PluginPackager` — implementation in Task 2)

**Produces for Task 3:**
- `ManifestV2` (parsed and validated)
- `IPluginPackager` (interface)
- `IPluginInstaller` (interface — stub, implementation in Task 3)
- `ManifestV2Validator.Validate(ManifestV2)` → `string?`
- `SdkVersionConstraintParser.Satisfies(string, Version)` → `bool`
- `PackagingOptions`
- `PackageInstallResult`
- `PluginPackagingException`

---

## Steps

### Step 1 — Failing tests first: ManifestV2Validator

- [ ] Create `tests/MSOSync.PluginTests/Packaging/ManifestV2ValidatorTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Plugin.Packaging;
using MSOSync.Plugin.Packaging.Models;
using Xunit;

namespace MSOSync.PluginTests.Packaging;

public sealed class ManifestV2ValidatorTests
{
    private static ManifestV2 Valid() => new()
    {
        ManifestVersion      = 2,
        Id                   = "test.plugin",
        Name                 = "Test Plugin",
        Version              = "1.0.0",
        SdkVersion           = "1.0",
        SdkVersionConstraint = ">=1.0.0 <2.0.0",
        ApiVersion           = "1",
        MinHostVersion       = "1.0.0",
        MaxHostVersion       = "99.0.0",
        EntryAssembly        = "Test.dll",
        EntryType            = "Test.Plugin",
        Author               = "Test Author",
        Description          = "A test plugin.",
        Files = [new PackageFileEntry { Path = "Test.dll", Sha256 = new string('a', 64) }],
    };

    [Fact]
    public void Validate_ValidManifest_ReturnsNull()
        => ManifestV2Validator.Validate(Valid()).Should().BeNull();

    [Fact]
    public void Validate_ManifestVersionNot2_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { ManifestVersion = 1 })
            .Should().Contain("manifestVersion");

    [Theory]
    [InlineData("id",                   "")]
    [InlineData("name",                 "")]
    [InlineData("version",              "")]
    [InlineData("sdkVersion",           "")]
    [InlineData("sdkVersionConstraint", "")]
    [InlineData("apiVersion",           "")]
    [InlineData("minHostVersion",       "")]
    [InlineData("maxHostVersion",       "")]
    [InlineData("entryAssembly",        "")]
    [InlineData("entryType",            "")]
    [InlineData("author",               "")]
    [InlineData("description",          "")]
    public void Validate_MissingRequiredField_ReturnsError(string field, string _)
    {
        var m = field switch
        {
            "id"                   => Valid() with { Id                   = "" },
            "name"                 => Valid() with { Name                 = "" },
            "version"              => Valid() with { Version              = "" },
            "sdkVersion"           => Valid() with { SdkVersion           = "" },
            "sdkVersionConstraint" => Valid() with { SdkVersionConstraint = "" },
            "apiVersion"           => Valid() with { ApiVersion           = "" },
            "minHostVersion"       => Valid() with { MinHostVersion       = "" },
            "maxHostVersion"       => Valid() with { MaxHostVersion       = "" },
            "entryAssembly"        => Valid() with { EntryAssembly        = "" },
            "entryType"            => Valid() with { EntryType            = "" },
            "author"               => Valid() with { Author               = "" },
            "description"          => Valid() with { Description          = "" },
            _                      => throw new ArgumentException(field),
        };
        ManifestV2Validator.Validate(m).Should().NotBeNull().And.Contain(field);
    }

    [Fact]
    public void Validate_MissingId_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { Id = "" }).Should().Contain("id");

    [Fact]
    public void Validate_InvalidVersionFormat_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { Version = "not-a-version" })
            .Should().Contain("version");

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("sub/dir/Evil.dll")]
    [InlineData("sub\\dir\\Evil.dll")]
    public void Validate_PathTraversalInEntryAssembly_ReturnsError(string badPath)
        => ManifestV2Validator.Validate(Valid() with { EntryAssembly = badPath })
            .Should().Contain("entryAssembly");

    [Theory]
    [InlineData("../evil")]
    [InlineData("sub/dir")]
    public void Validate_PathTraversalInId_ReturnsError(string badId)
        => ManifestV2Validator.Validate(Valid() with { Id = badId })
            .Should().Contain("id");

    [Fact]
    public void Validate_EmptyFiles_ReturnsError()
        => ManifestV2Validator.Validate(Valid() with { Files = [] })
            .Should().Contain("files");

    [Fact]
    public void Validate_DuplicateFilePaths_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "Test.dll", Sha256 = new string('a', 64) };
        var m = Valid() with { Files = [entry, entry] };
        ManifestV2Validator.Validate(m).Should().Contain("duplicate");
    }

    [Fact]
    public void Validate_InvalidSha256Length_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "Test.dll", Sha256 = new string('a', 63) };
        var m = Valid() with { Files = [entry] };
        ManifestV2Validator.Validate(m).Should().Contain("sha256");
    }

    [Fact]
    public void Validate_InvalidSha256NotHex_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "Test.dll", Sha256 = new string('z', 64) };
        var m = Valid() with { Files = [entry] };
        ManifestV2Validator.Validate(m).Should().Contain("sha256");
    }

    [Fact]
    public void Validate_TooManyKeywords_ReturnsError()
    {
        var m = Valid() with { Keywords = Enumerable.Range(0, 11).Select(i => $"kw{i}").ToList() };
        ManifestV2Validator.Validate(m).Should().Contain("keywords");
    }

    [Fact]
    public void Validate_PathTraversalInFilesEntry_ReturnsError()
    {
        var entry = new PackageFileEntry { Path = "../evil.dll", Sha256 = new string('a', 64) };
        var m = Valid() with { Files = [entry] };
        ManifestV2Validator.Validate(m).Should().Contain("files");
    }

    [Fact]
    public void Validate_InvalidVersionRange_InPluginDependencies_ReturnsError()
    {
        var dep = new PluginDependencyEntry { Id = "some.dep", VersionRange = "##invalid" };
        var m = Valid() with { PluginDependencies = [dep] };
        ManifestV2Validator.Validate(m).Should().Contain("versionRange");
    }
}
```

### Step 2 — Failing tests: SdkVersionConstraintParser

- [ ] Create `tests/MSOSync.PluginTests/Packaging/SdkVersionConstraintParserTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Plugin.Packaging;
using Xunit;

namespace MSOSync.PluginTests.Packaging;

public sealed class SdkVersionConstraintParserTests
{
    [Fact]
    public void Satisfies_GreaterThanOrEqual_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0", new Version(1, 2, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_GreaterThanOrEqual_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies(">=2.0.0", new Version(1, 9, 9)).Should().BeFalse();

    [Fact]
    public void Satisfies_StrictLessThan_Satisfied()
        => SdkVersionConstraintParser.Satisfies("<2.0.0", new Version(1, 9, 9)).Should().BeTrue();

    [Fact]
    public void Satisfies_StrictLessThan_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies("<2.0.0", new Version(2, 0, 0)).Should().BeFalse();

    [Fact]
    public void Satisfies_Range_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0 <2.0.0", new Version(1, 5, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_Range_ExactLowerBound_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0 <2.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_Range_UpperBoundExclusive()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0 <2.0.0", new Version(2, 0, 0)).Should().BeFalse();

    [Fact]
    public void Satisfies_ExactMatch_WithEquals_Satisfied()
        => SdkVersionConstraintParser.Satisfies("=1.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_ExactMatch_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies("=1.0.0", new Version(1, 0, 1)).Should().BeFalse();

    [Fact]
    public void Satisfies_BareVersion_ExactMatch_Satisfied()
        => SdkVersionConstraintParser.Satisfies("1.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_StrictGreaterThan_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">1.0.0", new Version(1, 0, 1)).Should().BeTrue();

    [Fact]
    public void Satisfies_StrictGreaterThan_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies(">1.0.0", new Version(1, 0, 0)).Should().BeFalse();

    [Fact]
    public void Satisfies_LessThanOrEqual_Satisfied()
        => SdkVersionConstraintParser.Satisfies("<=1.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_LessThanOrEqual_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies("<=1.0.0", new Version(1, 0, 1)).Should().BeFalse();

    [Fact]
    public void Satisfies_InvalidConstraint_ReturnsFalse()
        => SdkVersionConstraintParser.Satisfies("banana", new Version(1, 0, 0)).Should().BeFalse();

    [Fact]
    public void Parse_InvalidConstraint_ReturnsNull()
        => SdkVersionConstraintParser.Parse("banana").Should().BeNull();
}
```

### Step 3 — Failing tests: PluginPackager

- [ ] Create `tests/MSOSync.PluginTests/Packaging/PluginPackagerTests.cs`:

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Packaging.Packager;
using MSOSync.Plugin.Signing.Abstractions;
using Xunit;

namespace MSOSync.PluginTests.Packaging;

public sealed class PluginPackagerTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _outputDir;

    public PluginPackagerTests()
    {
        var id = Guid.NewGuid().ToString("N");
        _sourceDir = Path.Combine(Path.GetTempPath(), $"msopkg-src-{id}");
        _outputDir = Path.Combine(Path.GetTempPath(), $"msopkg-out-{id}");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, true);
    }

    private PluginPackager MakePackager(IPluginSigner? signer = null)
        => new(
            Options.Create(new PackagingOptions()),
            Options.Create(new PluginHostOptions { SupportedSdkMajorVersion = "1", SupportedApiVersion = "1" }),
            NullLogger<PluginPackager>.Instance);

    private void WriteValidSource(string entryDllName = "Test.dll")
    {
        var dllBytes = new byte[] { 0x4D, 0x5A }; // MZ header stub
        File.WriteAllBytes(Path.Combine(_sourceDir, entryDllName), dllBytes);

        var manifest = new
        {
            manifestVersion      = 2,
            id                   = "test.plugin",
            name                 = "Test Plugin",
            version              = "1.0.0",
            sdkVersion           = "1.0",
            sdkVersionConstraint = ">=1.0.0 <2.0.0",
            apiVersion           = "1",
            minHostVersion       = "1.0.0",
            maxHostVersion       = "99.0.0",
            entryAssembly        = entryDllName,
            entryType            = "Test.Plugin",
            author               = "Test Author",
            description          = "A test plugin.",
        };
        File.WriteAllText(
            Path.Combine(_sourceDir, "plugin.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task PackageAsync_ValidSourceDir_CreatesZipFile()
    {
        WriteValidSource();
        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "test.plugin.msopkg");

        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);

        File.Exists(output).Should().BeTrue();
        new FileInfo(output).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PackageAsync_ValidSourceDir_OutputExtensionIsMsopkg()
    {
        WriteValidSource();
        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "test.plugin.msopkg");

        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);

        Path.GetExtension(output).Should().Be(".msopkg");
    }

    [Fact]
    public async Task PackageAsync_MissingManifest_ThrowsPackagingException()
    {
        // source dir has no plugin.json or manifest.json
        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "bad.msopkg");

        var act = () => packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);

        await act.Should().ThrowAsync<PluginPackagingException>()
            .WithMessage("*manifest*");
    }

    [Fact]
    public async Task PackageAsync_EntryAssemblyNotFound_ThrowsPackagingException()
    {
        // write manifest referencing missing DLL
        var manifest = new
        {
            manifestVersion      = 2,
            id                   = "test.plugin",
            name                 = "Test",
            version              = "1.0.0",
            sdkVersion           = "1.0",
            sdkVersionConstraint = ">=1.0.0 <2.0.0",
            apiVersion           = "1",
            minHostVersion       = "1.0.0",
            maxHostVersion       = "99.0.0",
            entryAssembly        = "Missing.dll",
            entryType            = "Test.Plugin",
            author               = "Author",
            description          = "Desc.",
        };
        File.WriteAllText(
            Path.Combine(_sourceDir, "plugin.json"),
            JsonSerializer.Serialize(manifest));

        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "bad.msopkg");

        var act = () => packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);

        await act.Should().ThrowAsync<PluginPackagingException>();
    }

    [Fact]
    public async Task PackageAsync_Unsigned_ManifestJsonInsideArchive_HasNoSignatureBlock()
    {
        WriteValidSource();
        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "test.plugin.msopkg");

        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);

        using var zip     = ZipFile.OpenRead(output);
        var manifestEntry = zip.GetEntry("manifest.json")!;
        using var stream  = manifestEntry.Open();
        using var reader  = new StreamReader(stream);
        var json          = await reader.ReadToEndAsync();
        var doc           = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("signature", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PackageAsync_WithSigningKey_ManifestContainsSignatureBlock()
    {
        WriteValidSource();

        var signerMock = new Mock<IPluginSigner>();
        signerMock.Setup(s => s.PublicKeyId).Returns("test-key-01");
        signerMock.Setup(s => s.Sign(It.IsAny<ReadOnlySpan<byte>>())).Returns("AAAA==");

        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "test.plugin.msopkg");

        await packager.PackageAsync(_sourceDir, output, signerMock.Object, CancellationToken.None);

        using var zip     = ZipFile.OpenRead(output);
        var manifestEntry = zip.GetEntry("manifest.json")!;
        using var stream  = manifestEntry.Open();
        using var reader  = new StreamReader(stream);
        var json          = await reader.ReadToEndAsync();
        var doc           = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("signature", out var sig).Should().BeTrue();
        sig.GetProperty("algorithm").GetString().Should().Be("RSA-PSS-SHA256");
        sig.GetProperty("publicKeyId").GetString().Should().Be("test-key-01");
    }

    [Fact]
    public async Task PackageAsync_FileHashesInManifest_MatchActualFiles()
    {
        WriteValidSource("Test.dll");
        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "test.plugin.msopkg");

        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);

        using var zip     = ZipFile.OpenRead(output);
        var manifestEntry = zip.GetEntry("manifest.json")!;
        using var mStream = manifestEntry.Open();
        using var reader  = new StreamReader(mStream);
        var json          = await reader.ReadToEndAsync();
        var doc           = JsonDocument.Parse(json);
        var files         = doc.RootElement.GetProperty("files");

        foreach (var fileEntry in files.EnumerateArray())
        {
            var path          = fileEntry.GetProperty("path").GetString()!;
            var expectedHash  = fileEntry.GetProperty("sha256").GetString()!;
            var archiveEntry  = zip.GetEntry(path)!;
            using var fStream = archiveEntry.Open();
            using var ms      = new MemoryStream();
            await fStream.CopyToAsync(ms);
            var actualHash    = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
            actualHash.Should().Be(expectedHash, $"hash mismatch for {path}");
        }
    }
}
```

### Step 4 — Implement ManifestV2 models

- [ ] Create `src/MSOSync.Plugin/Packaging/Models/ManifestV2.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record ManifestV2
{
    [JsonPropertyName("manifestVersion")]      public int     ManifestVersion      { get; init; } = 2;
    [JsonPropertyName("id")]                   public string  Id                   { get; init; } = null!;
    [JsonPropertyName("name")]                 public string  Name                 { get; init; } = null!;
    [JsonPropertyName("version")]              public string  Version              { get; init; } = null!;
    [JsonPropertyName("sdkVersion")]           public string  SdkVersion           { get; init; } = null!;
    [JsonPropertyName("sdkVersionConstraint")] public string  SdkVersionConstraint { get; init; } = null!;
    [JsonPropertyName("apiVersion")]           public string  ApiVersion           { get; init; } = null!;
    [JsonPropertyName("startupOrder")]         public int     StartupOrder         { get; init; } = 1000;
    [JsonPropertyName("minHostVersion")]       public string  MinHostVersion       { get; init; } = null!;
    [JsonPropertyName("maxHostVersion")]       public string  MaxHostVersion       { get; init; } = null!;
    [JsonPropertyName("entryAssembly")]        public string  EntryAssembly        { get; init; } = null!;
    [JsonPropertyName("entryType")]            public string  EntryType            { get; init; } = null!;
    [JsonPropertyName("author")]               public string  Author               { get; init; } = null!;
    [JsonPropertyName("authorEmail")]          public string? AuthorEmail          { get; init; }
    [JsonPropertyName("homepage")]             public string? Homepage             { get; init; }
    [JsonPropertyName("license")]              public string? License              { get; init; }
    [JsonPropertyName("description")]          public string  Description          { get; init; } = null!;
    [JsonPropertyName("keywords")]             public IReadOnlyList<string>              Keywords            { get; init; } = [];
    [JsonPropertyName("capabilities")]         public IReadOnlyList<string>              Capabilities        { get; init; } = [];
    [JsonPropertyName("permissions")]          public IReadOnlyList<string>              Permissions         { get; init; } = [];
    [JsonPropertyName("pluginDependencies")]   public IReadOnlyList<PluginDependencyEntry> PluginDependencies { get; init; } = [];
    [JsonPropertyName("files")]                public IReadOnlyList<PackageFileEntry>    Files               { get; init; } = [];
    [JsonPropertyName("signature")]            public ManifestSignatureBlock?            Signature           { get; init; }
}
```

- [ ] Create `src/MSOSync.Plugin/Packaging/Models/ManifestSignatureBlock.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record ManifestSignatureBlock
{
    [JsonPropertyName("algorithm")]   public string Algorithm   { get; init; } = null!;
    [JsonPropertyName("publicKeyId")] public string PublicKeyId { get; init; } = null!;
    [JsonPropertyName("value")]       public string Value       { get; init; } = null!;
}
```

- [ ] Create `src/MSOSync.Plugin/Packaging/Models/PackageFileEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record PackageFileEntry
{
    [JsonPropertyName("path")]   public string Path   { get; init; } = null!;
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = null!;
}
```

- [ ] Create `src/MSOSync.Plugin/Packaging/Models/PluginDependencyEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Plugin.Packaging.Models;

public sealed record PluginDependencyEntry
{
    [JsonPropertyName("id")]           public string Id           { get; init; } = null!;
    [JsonPropertyName("versionRange")] public string VersionRange { get; init; } = null!;
}
```

- [ ] Create `src/MSOSync.Plugin/Packaging/Models/PackagingOptions.cs`:

```csharp
namespace MSOSync.Plugin.Packaging.Models;

public sealed class PackagingOptions
{
    /// <summary>Maximum uncompressed size of a .msopkg archive in bytes. Default: 50 MB.</summary>
    public long MaxPackageSizeBytes { get; set; } = 52_428_800;

    /// <summary>Maximum number of entries inside the archive. Default: 200.</summary>
    public int MaxFileCount { get; set; } = 200;

    /// <summary>Maximum total uncompressed size of the assets/ directory in bytes. Default: 2 MB.</summary>
    public long MaxAssetsSizeBytes { get; set; } = 2_097_152;

    /// <summary>Maximum number of files inside assets/. Default: 20.</summary>
    public int MaxAssetsFileCount { get; set; } = 20;
}
```

- [ ] Create `src/MSOSync.Plugin/Packaging/Models/PackageInstallResult.cs`:

```csharp
namespace MSOSync.Plugin.Packaging.Models;

public sealed record PackageInstallResult(
    bool    Success,
    string  PluginId,
    string? InstalledVersion,
    string? FailureStage,
    string? ErrorMessage)
{
    public static PackageInstallResult Ok(string pluginId, string version)
        => new(true, pluginId, version, null, null);

    public static PackageInstallResult Fail(string pluginId, string stage, string error)
        => new(false, pluginId, null, stage, error);
}
```

### Step 5 — Implement PluginPackagingException

- [ ] Create `src/MSOSync.Plugin/Packaging/PluginPackagingException.cs`:

```csharp
namespace MSOSync.Plugin.Packaging;

public sealed class PluginPackagingException : Exception
{
    public string Stage { get; }

    public PluginPackagingException(string stage, string message)
        : base($"[{stage}] {message}")
        => Stage = stage;

    public PluginPackagingException(string stage, string message, Exception inner)
        : base($"[{stage}] {message}", inner)
        => Stage = stage;
}
```

### Step 6 — Implement SdkVersionConstraintParser

- [ ] Create `src/MSOSync.Plugin/Packaging/SdkVersionConstraintParser.cs`:

```csharp
namespace MSOSync.Plugin.Packaging;

/// <summary>
/// Parses a minimal npm-style semver range into a Version predicate.
/// Supported forms: >=X.Y.Z  >X.Y.Z  <=X.Y.Z  &lt;X.Y.Z  =X.Y.Z  X.Y.Z  (space-AND of two comparators).
/// </summary>
public static class SdkVersionConstraintParser
{
    /// <summary>
    /// Parse <paramref name="constraint"/> into a predicate.
    /// Returns null if the constraint string is invalid or unparseable.
    /// </summary>
    public static Func<Version, bool>? Parse(string constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint)) return null;

        var parts = constraint.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 1)
            return ParseComparator(parts[0]);

        if (parts.Length == 2)
        {
            var left  = ParseComparator(parts[0]);
            var right = ParseComparator(parts[1]);
            if (left is null || right is null) return null;
            return v => left(v) && right(v);
        }

        return null;
    }

    /// <summary>
    /// Returns true if <paramref name="hostVersion"/> satisfies <paramref name="constraint"/>.
    /// Returns false if the constraint is unparseable (treated as incompatible).
    /// </summary>
    public static bool Satisfies(string constraint, Version hostVersion)
        => Parse(constraint)?.Invoke(hostVersion) ?? false;

    private static Func<Version, bool>? ParseComparator(string part)
    {
        (string op, string vstr) = part switch
        {
            _ when part.StartsWith(">=", StringComparison.Ordinal) => (">=", part[2..]),
            _ when part.StartsWith(">",  StringComparison.Ordinal)
                && !part.StartsWith(">=", StringComparison.Ordinal) => (">",  part[1..]),
            _ when part.StartsWith("<=", StringComparison.Ordinal) => ("<=", part[2..]),
            _ when part.StartsWith("<",  StringComparison.Ordinal)
                && !part.StartsWith("<=", StringComparison.Ordinal) => ("<",  part[1..]),
            _ when part.StartsWith("=",  StringComparison.Ordinal) => ("=",  part[1..]),
            _ => ("=", part),  // bare version treated as exact match
        };

        if (!Version.TryParse(EnsureThreeParts(vstr), out var v)) return null;

        return op switch
        {
            ">=" => host => host >= v,
            ">"  => host => host >  v,
            "<=" => host => host <= v,
            "<"  => host => host <  v,
            "="  => host => host == v,
            _    => null,
        };
    }

    // Ensure at least major.minor.patch so Version.TryParse works consistently.
    private static string EnsureThreeParts(string v)
    {
        var parts = v.Split('.');
        return parts.Length switch
        {
            1 => $"{v}.0.0",
            2 => $"{v}.0",
            _ => v,
        };
    }
}
```

### Step 7 — Implement ManifestV2Validator

- [ ] Create `src/MSOSync.Plugin/Packaging/ManifestV2Validator.cs`:

```csharp
using MSOSync.Plugin.Packaging.Models;

namespace MSOSync.Plugin.Packaging;

/// <summary>
/// Validates a parsed <see cref="ManifestV2"/>. Returns null on success, or an error message on the first failure.
/// </summary>
public static class ManifestV2Validator
{
    private static readonly char[] PathSeparators = ['/', '\\'];
    private static readonly System.Text.RegularExpressions.Regex HexRegex =
        new("^[0-9a-f]{64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string? Validate(ManifestV2? manifest)
    {
        if (manifest is null) return "Manifest is null after deserialization.";

        if (manifest.ManifestVersion != 2)
            return "Field 'manifestVersion' must be 2 for packaged plugins.";

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return "Field 'id' is required.";
        if (manifest.Id.IndexOfAny(PathSeparators) >= 0 || manifest.Id.Contains(".."))
            return "Field 'id' must not contain path separators or '..'.";

        if (string.IsNullOrWhiteSpace(manifest.Name))
            return "Field 'name' is required.";

        if (string.IsNullOrWhiteSpace(manifest.Version))
            return "Field 'version' is required.";
        if (!Version.TryParse(manifest.Version, out _))
            return $"Field 'version' value '{manifest.Version}' is not a valid version (major.minor.patch).";

        if (string.IsNullOrWhiteSpace(manifest.SdkVersion))
            return "Field 'sdkVersion' is required.";

        if (string.IsNullOrWhiteSpace(manifest.SdkVersionConstraint))
            return "Field 'sdkVersionConstraint' is required.";

        if (string.IsNullOrWhiteSpace(manifest.ApiVersion))
            return "Field 'apiVersion' is required.";
        if (!int.TryParse(manifest.ApiVersion, out _))
            return $"Field 'apiVersion' value '{manifest.ApiVersion}' is not a valid integer string.";

        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion))
            return "Field 'minHostVersion' is required.";
        if (string.IsNullOrWhiteSpace(manifest.MaxHostVersion))
            return "Field 'maxHostVersion' is required.";

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            return "Field 'entryAssembly' is required.";
        if (manifest.EntryAssembly.IndexOfAny(PathSeparators) >= 0 || manifest.EntryAssembly.Contains(".."))
            return "Field 'entryAssembly' must be a filename only, not a path.";

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
            return "Field 'entryType' is required.";
        if (manifest.EntryType.IndexOfAny(PathSeparators) >= 0 || manifest.EntryType.Contains(".."))
            return "Field 'entryType' must not contain path separators or '..'.";

        if (string.IsNullOrWhiteSpace(manifest.Author))
            return "Field 'author' is required.";

        if (string.IsNullOrWhiteSpace(manifest.Description))
            return "Field 'description' is required.";

        if (manifest.Keywords.Count > 10)
            return "Field 'keywords' must have at most 10 entries.";

        // Validate files[]
        if (manifest.Files.Count == 0)
            return "Field 'files' must contain at least one entry.";

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
                return "Each entry in 'files' must have a non-empty 'path'.";
            if (entry.Path.Contains("..") || System.IO.Path.IsPathRooted(entry.Path))
                return $"Field 'files[].path' must be a relative path without '..': '{entry.Path}'.";
            if (!seenPaths.Add(entry.Path))
                return $"Field 'files' contains duplicate path: '{entry.Path}'.";
            if (string.IsNullOrWhiteSpace(entry.Sha256) || !HexRegex.IsMatch(entry.Sha256))
                return $"Field 'files[].sha256' for '{entry.Path}' must be exactly 64 lowercase hex characters.";
        }

        // Validate pluginDependencies[]
        foreach (var dep in manifest.PluginDependencies)
        {
            if (string.IsNullOrWhiteSpace(dep.Id))
                return "Each entry in 'pluginDependencies' must have a non-empty 'id'.";
            if (string.IsNullOrWhiteSpace(dep.VersionRange))
                return $"'pluginDependencies[{dep.Id}].versionRange' is required.";
            if (SdkVersionConstraintParser.Parse(dep.VersionRange) is null)
                return $"'pluginDependencies[{dep.Id}].versionRange' value '{dep.VersionRange}' is not a valid semver range.";
        }

        return null;
    }
}
```

### Step 8 — Run failing unit tests (confirm they fail)

- [ ] Run: `dotnet test tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj --filter "FullyQualifiedName~ManifestV2Validator|FullyQualifiedName~SdkVersionConstraint" --no-build 2>&1 | Select-Object -Last 20`

  Tests should fail with compile errors (types not yet defined — expected).

### Step 9 — Implement IPluginSigner interface (stub for Task 2)

- [ ] Create `src/MSOSync.Plugin/Signing/Abstractions/IPluginSigner.cs`:

```csharp
namespace MSOSync.Plugin.Signing.Abstractions;

/// <summary>
/// Signs a canonical manifest hash using the configured private key.
/// Used by <see cref="MSOSync.Plugin.Packaging.Packager.PluginPackager"/> when a signing key is provided.
/// Implementation: <see cref="MSOSync.Plugin.Signing.RsaPssPluginSigner"/> (Task 2).
/// </summary>
public interface IPluginSigner
{
    /// <summary>
    /// Sign the given data with the private RSA key.
    /// </summary>
    /// <param name="data">32-byte SHA-256 hash of the canonical manifest JSON (without signature block).</param>
    /// <returns>Base64-standard-encoded RSA-PSS-SHA256 signature bytes.</returns>
    string Sign(ReadOnlySpan<byte> data);

    /// <summary>Identifier of the public key stored in manifest.signature.publicKeyId.</summary>
    string PublicKeyId { get; }
}
```

### Step 10 — Implement IPluginPackager interface

- [ ] Create `src/MSOSync.Plugin/Packaging/Abstractions/IPluginPackager.cs`:

```csharp
using MSOSync.Plugin.Signing.Abstractions;

namespace MSOSync.Plugin.Packaging.Abstractions;

/// <summary>
/// Builds a .msopkg archive from a plugin project output directory.
/// </summary>
public interface IPluginPackager
{
    /// <summary>
    /// Create a .msopkg archive at <paramref name="outputPackagePath"/> from the files in
    /// <paramref name="pluginSourceDirectory"/>.
    /// </summary>
    /// <param name="pluginSourceDirectory">
    ///   Directory containing plugin.json or manifest.json (ManifestV2), the entry DLL,
    ///   optional lib/, and optional assets/. Must exist.
    /// </param>
    /// <param name="outputPackagePath">
    ///   Full path where the .msopkg file will be written. Parent directory must exist.
    /// </param>
    /// <param name="signingKey">
    ///   If provided, the resulting archive is signed. The manifest.json inside the archive
    ///   will include a populated signature block.
    ///   Pass null to produce an unsigned package (valid for local dev when
    ///   PluginSecurityOptions.RequireSignedPackages = false).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="PluginPackagingException">Thrown for any validation or IO failure.</exception>
    Task PackageAsync(
        string         pluginSourceDirectory,
        string         outputPackagePath,
        IPluginSigner? signingKey,
        CancellationToken ct);
}
```

### Step 11 — Implement IPluginInstaller interface (stub; implementation in Task 3)

- [ ] Create `src/MSOSync.Plugin/Packaging/Abstractions/IPluginInstaller.cs`:

```csharp
using MSOSync.Plugin.Packaging.Models;

namespace MSOSync.Plugin.Packaging.Abstractions;

/// <summary>
/// Installs a .msopkg archive into the configured plugins directory.
/// Implementation: <see cref="MSOSync.Plugin.Packaging.Installer.PluginInstaller"/> (Task 3).
/// </summary>
public interface IPluginInstaller
{
    /// <summary>
    /// Install (or upgrade) the plugin packaged in <paramref name="packagePath"/>.
    /// Never throws; all exceptions are caught and translated into a <see cref="PackageInstallResult.Fail"/> result.
    /// </summary>
    /// <param name="packagePath">Absolute path to the .msopkg file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PackageInstallResult> InstallAsync(string packagePath, CancellationToken ct);

    /// <summary>
    /// Remove an installed plugin by ID. Deletes plugins/{pluginId} directory and
    /// calls IPluginStore.SetEnabledAsync(pluginId, false).
    /// Returns false if the plugin directory does not exist.
    /// </summary>
    Task<bool> UninstallAsync(string pluginId, CancellationToken ct);
}
```

### Step 12 — Implement PluginPackager

- [ ] Create `src/MSOSync.Plugin/Packaging/Packager/PluginPackager.cs`:

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging.Abstractions;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Signing.Abstractions;

namespace MSOSync.Plugin.Packaging.Packager;

public sealed class PluginPackager(
    IOptions<PackagingOptions>  packagingOptions,
    IOptions<PluginHostOptions> hostOptions,
    ILogger<PluginPackager>     logger) : IPluginPackager
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    internal static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task PackageAsync(
        string         pluginSourceDirectory,
        string         outputPackagePath,
        IPluginSigner? signingKey,
        CancellationToken ct)
    {
        if (!Directory.Exists(pluginSourceDirectory))
            throw new PluginPackagingException("SourceValidation",
                $"Plugin source directory '{pluginSourceDirectory}' does not exist.");

        // Step 1: Read and parse manifest (accepts plugin.json OR manifest.json)
        var manifestPath = TryFindManifest(pluginSourceDirectory);
        if (manifestPath is null)
            throw new PluginPackagingException("ManifestParse",
                $"No 'plugin.json' or 'manifest.json' found in '{pluginSourceDirectory}'.");

        ManifestV2 manifest;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, ct);
            manifest = JsonSerializer.Deserialize<ManifestV2>(json, ReadOpts)
                       ?? throw new PluginPackagingException("ManifestParse", "Manifest deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new PluginPackagingException("ManifestParse", ex.Message, ex);
        }

        // Step 2: Validate manifest schema
        var validationError = ManifestV2Validator.Validate(manifest);
        if (validationError is not null)
            throw new PluginPackagingException("ManifestValidation", validationError);

        // Step 3: Verify entry DLL exists
        var entryDllPath = Path.Combine(pluginSourceDirectory, manifest.EntryAssembly);
        if (!File.Exists(entryDllPath))
            throw new PluginPackagingException("ManifestValidation",
                $"Entry assembly '{manifest.EntryAssembly}' not found in '{pluginSourceDirectory}'.");

        // Step 4: Collect files to hash (DLLs + plugin.config.json if present)
        var filesToHash = CollectHashableFiles(pluginSourceDirectory, manifest);

        // Step 5: Compute SHA-256 of each file
        var fileEntries = new List<PackageFileEntry>();
        foreach (var (relPath, absPath) in filesToHash)
        {
            var hash = await ComputeFileSha256Async(absPath, ct);
            fileEntries.Add(new PackageFileEntry { Path = relPath, Sha256 = hash });
        }

        // Step 6: Inject file hashes into manifest (preserve existing files[] if manifest already had them)
        manifest = manifest with { Files = fileEntries, Signature = null };

        // Step 7: Optionally sign
        if (signingKey is not null)
        {
            var canonicalJson = JsonSerializer.Serialize(manifest, CanonicalOpts);
            var hash          = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
            var sigValue      = signingKey.Sign(hash);
            manifest = manifest with
            {
                Signature = new ManifestSignatureBlock
                {
                    Algorithm   = "RSA-PSS-SHA256",
                    PublicKeyId = signingKey.PublicKeyId,
                    Value       = sigValue,
                },
            };
        }

        // Step 8: Write the .msopkg ZIP archive
        var opts  = packagingOptions.Value;
        var tmpOut = outputPackagePath + ".tmp";

        try
        {
            using (var zipStream = new FileStream(tmpOut, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive   = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                // manifest.json (final, with hashes and optional signature)
                var manifestJson  = JsonSerializer.Serialize(manifest, CanonicalOpts);
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                await using (var ms = manifestEntry.Open())
                    await ms.WriteAsync(Encoding.UTF8.GetBytes(manifestJson), ct);

                // All listed files
                foreach (var (relPath, absPath) in filesToHash)
                {
                    var entry = archive.CreateEntry(relPath, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var fileStream  = File.OpenRead(absPath);
                    await fileStream.CopyToAsync(entryStream, ct);
                }

                // assets/ (optional, not hash-verified)
                var assetsDir = Path.Combine(pluginSourceDirectory, "assets");
                if (Directory.Exists(assetsDir))
                {
                    int  assetCount    = 0;
                    long assetsSize    = 0;
                    foreach (var assetFile in Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories))
                    {
                        assetCount++;
                        var info = new FileInfo(assetFile);
                        assetsSize += info.Length;

                        if (assetCount > opts.MaxAssetsFileCount)
                            throw new PluginPackagingException("ArchiveValidation",
                                $"assets/ directory exceeds maximum file count of {opts.MaxAssetsFileCount}.");
                        if (assetsSize > opts.MaxAssetsSizeBytes)
                            throw new PluginPackagingException("ArchiveValidation",
                                $"assets/ directory exceeds maximum size of {opts.MaxAssetsSizeBytes} bytes.");

                        var relAssetPath = Path.GetRelativePath(pluginSourceDirectory, assetFile)
                                               .Replace('\\', '/');
                        var assetEntry   = archive.CreateEntry(relAssetPath, CompressionLevel.Optimal);
                        await using var aStream = assetEntry.Open();
                        await using var fStream = File.OpenRead(assetFile);
                        await fStream.CopyToAsync(aStream, ct);
                    }
                }
            }

            // Atomic rename
            if (File.Exists(outputPackagePath)) File.Delete(outputPackagePath);
            File.Move(tmpOut, outputPackagePath);

            logger.LogInformation(
                "Plugin packaged: '{Id}' v{Version} → {Output}",
                manifest.Id, manifest.Version, outputPackagePath);
        }
        catch
        {
            if (File.Exists(tmpOut)) File.Delete(tmpOut);
            throw;
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string? TryFindManifest(string dir)
    {
        // prefer manifest.json (v2), fall back to plugin.json
        var v2 = Path.Combine(dir, "manifest.json");
        if (File.Exists(v2)) return v2;
        var v1 = Path.Combine(dir, "plugin.json");
        if (File.Exists(v1)) return v1;
        return null;
    }

    private static List<(string RelPath, string AbsPath)> CollectHashableFiles(
        string sourceDir, ManifestV2 manifest)
    {
        var result = new List<(string, string)>();

        // Entry DLL (always hashed, at archive root)
        var entryAbs = Path.Combine(sourceDir, manifest.EntryAssembly);
        result.Add((manifest.EntryAssembly, entryAbs));

        // lib/*.dll
        var libDir = Path.Combine(sourceDir, "lib");
        if (Directory.Exists(libDir))
        {
            foreach (var dll in Directory.EnumerateFiles(libDir, "*.dll"))
            {
                var rel = "lib/" + Path.GetFileName(dll);
                result.Add((rel, dll));
            }
        }

        // plugin.config.json (optional)
        var configPath = Path.Combine(sourceDir, "plugin.config.json");
        if (File.Exists(configPath))
            result.Add(("plugin.config.json", configPath));

        return result;
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct)
    {
        using var hash   = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var       buffer = new byte[4096];
        await using var fs = File.OpenRead(path);
        int read;
        while ((read = await fs.ReadAsync(buffer, ct)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant();
    }
}
```

### Step 13 — Build and run unit tests

- [ ] Run: `dotnet build src/MSOSync.Plugin/MSOSync.Plugin.csproj -c Debug 2>&1 | Select-Object -Last 30`

  Expected: build succeeds with 0 errors.

- [ ] Run: `dotnet test tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj --filter "FullyQualifiedName~ManifestV2ValidatorTests|FullyQualifiedName~SdkVersionConstraintParserTests|FullyQualifiedName~PluginPackagerTests" -c Debug 2>&1 | Select-Object -Last 40`

  Expected: all tests pass.

### Step 14 — Commit

- [ ] Stage files:

```
git add src/MSOSync.Plugin/Packaging/Models/ManifestV2.cs
git add src/MSOSync.Plugin/Packaging/Models/ManifestSignatureBlock.cs
git add src/MSOSync.Plugin/Packaging/Models/PackageFileEntry.cs
git add src/MSOSync.Plugin/Packaging/Models/PluginDependencyEntry.cs
git add src/MSOSync.Plugin/Packaging/Models/PackagingOptions.cs
git add src/MSOSync.Plugin/Packaging/Models/PackageInstallResult.cs
git add src/MSOSync.Plugin/Packaging/PluginPackagingException.cs
git add src/MSOSync.Plugin/Packaging/ManifestV2Validator.cs
git add src/MSOSync.Plugin/Packaging/SdkVersionConstraintParser.cs
git add src/MSOSync.Plugin/Packaging/Abstractions/IPluginPackager.cs
git add src/MSOSync.Plugin/Packaging/Abstractions/IPluginInstaller.cs
git add src/MSOSync.Plugin/Signing/Abstractions/IPluginSigner.cs
git add src/MSOSync.Plugin/Packaging/Packager/PluginPackager.cs
git add tests/MSOSync.PluginTests/Packaging/ManifestV2ValidatorTests.cs
git add tests/MSOSync.PluginTests/Packaging/SdkVersionConstraintParserTests.cs
git add tests/MSOSync.PluginTests/Packaging/PluginPackagerTests.cs
```

- [ ] Commit:

```
git commit -m "feat(2C.1-T1): ManifestV2 models, validator, SDK constraint parser, IPluginPackager + PluginPackager"
```
