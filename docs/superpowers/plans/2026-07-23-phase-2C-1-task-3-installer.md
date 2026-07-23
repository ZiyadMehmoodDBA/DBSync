# Task 3 — IPluginInstaller, PluginInstaller, M036 Migration, Store/Entity Extensions

**Phase:** 2C.1
**Depends on:** Task 1 (ManifestV2, IPluginInstaller interface, PluginPackager), Task 2 (IPluginSignatureVerifier, signing models)
**Produces:** `PluginInstaller` implementation, M036 migration, extended `SyncPlugin` entity, extended `PluginRecord` model, extended `IPluginStore` interface, unit tests for installer

---

## Files to Create / Modify

| File | Action |
|------|--------|
| `src/MSOSync.Persistence/Entities/SyncPlugin.cs` | Modify — add 4 new nullable columns |
| `src/MSOSync.Plugin/Models/PluginRecord.cs` | Modify — add 4 new properties |
| `src/MSOSync.Plugin/Abstractions/IPluginStore.cs` | Modify — add `GetByIdAsync` + `DeleteAsync` |
| `src/MSOSync.Persistence/Migrations/M036_PluginPackagingColumns.cs` | Create |
| `src/MSOSync.Plugin/Packaging/Installer/PluginInstaller.cs` | Create |
| `tests/MSOSync.PluginTests/Packaging/PluginInstallerTests.cs` | Create |

---

## Interfaces

**Consumes (from T1):**
- `ManifestV2`, `ManifestV2Validator`, `SdkVersionConstraintParser`, `PackageInstallResult`
- `IPluginInstaller` (interface stub)
- `PackagingOptions`
- `PluginPackagingException`
- `CanonicalOpts` (from `PluginPackager` — exposed as `internal static`)

**Consumes (from T2):**
- `IPluginSignatureVerifier`
- `PluginSecurityOptions`
- `SignatureVerificationOutcome`

**Consumes (existing):**
- `IPluginStore.UpsertAsync` / `SetEnabledAsync`
- `PluginManifest` (v1) — for deriving the backward-compat `plugin.json`
- `PluginHostOptions.PluginsPath`, `PluginHostOptions.HostVersion`, `PluginHostOptions.SupportedSdkMajorVersion`

**Produces:**
- `PluginInstaller` (implements `IPluginInstaller`)
- Extended `SyncPlugin` entity, `PluginRecord`, `IPluginStore`
- M036 migration SQL

---

## Steps

### Step 1 — Failing tests first: PluginInstallerTests

- [ ] Create `tests/MSOSync.PluginTests/Packaging/PluginInstallerTests.cs`:

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging;
using MSOSync.Plugin.Packaging.Installer;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing;
using MSOSync.Plugin.Signing.Abstractions;
using MSOSync.Plugin.Signing.Models;
using Xunit;

namespace MSOSync.PluginTests.Packaging;

public sealed class PluginInstallerTests : IDisposable
{
    private readonly string _pluginsDir;
    private readonly string _packagesDir;
    private readonly Mock<IPluginStore> _storeMock;

    private static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public PluginInstallerTests()
    {
        var id       = Guid.NewGuid().ToString("N");
        _pluginsDir  = Path.Combine(Path.GetTempPath(), $"msopkg-plugins-{id}");
        _packagesDir = Path.Combine(Path.GetTempPath(), $"msopkg-pkgs-{id}");
        Directory.CreateDirectory(_pluginsDir);
        Directory.CreateDirectory(_packagesDir);

        _storeMock = new Mock<IPluginStore>();
        _storeMock
            .Setup(s => s.UpsertAsync(It.IsAny<PluginRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storeMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock
            .Setup(s => s.SetEnabledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        if (Directory.Exists(_pluginsDir))  Directory.Delete(_pluginsDir, true);
        if (Directory.Exists(_packagesDir)) Directory.Delete(_packagesDir, true);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private PluginInstaller MakeInstaller(
        IPluginSignatureVerifier? verifier   = null,
        PluginSecurityOptions?    secOptions = null)
    {
        var sec      = secOptions ?? new PluginSecurityOptions { RequireSignedPackages = false };
        var hostOpts = new PluginHostOptions
        {
            PluginsPath              = _pluginsDir,
            HostVersion              = "15.0.0",
            SupportedSdkMajorVersion = "1",
            SupportedApiVersion      = "1",
        };
        verifier ??= BuildNoopVerifier();
        return new PluginInstaller(
            _storeMock.Object,
            verifier,
            Options.Create(sec),
            Options.Create(hostOpts),
            Options.Create(new PackagingOptions()),
            NullLogger<PluginInstaller>.Instance);
    }

    private static IPluginSignatureVerifier BuildNoopVerifier()
    {
        var mock = new Mock<IPluginSignatureVerifier>();
        mock.Setup(v => v.Verify(It.IsAny<ManifestV2>(), It.IsAny<string>()))
            .Returns(SignatureVerificationResult.NoSignature());
        return mock.Object;
    }

    private static (byte[] dllBytes, string dllHash) MakeDll()
    {
        var bytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };
        var hash  = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (bytes, hash);
    }

    /// <summary>Builds a minimal in-memory .msopkg archive.</summary>
    private string BuildPackage(
        string pluginId            = "test.plugin",
        string version             = "1.0.0",
        string sdkConstraint       = ">=1.0.0 <2.0.0",
        byte[]? dllContent         = null,
        string? dllHash            = null,
        ManifestSignatureBlock? sig = null,
        bool includeDllInArchive   = true,
        bool corruptDll            = false)
    {
        var (defaultDll, defaultHash) = MakeDll();
        dllContent ??= defaultDll;
        dllHash    ??= defaultHash;

        var manifest = new ManifestV2
        {
            ManifestVersion      = 2,
            Id                   = pluginId,
            Name                 = "Test Plugin",
            Version              = version,
            SdkVersion           = "1.0",
            SdkVersionConstraint = sdkConstraint,
            ApiVersion           = "1",
            MinHostVersion       = "1.0.0",
            MaxHostVersion       = "99.0.0",
            EntryAssembly        = "Test.dll",
            EntryType            = "Test.Plugin",
            Author               = "Author",
            Description          = "Desc.",
            Files                = [new PackageFileEntry { Path = "Test.dll", Sha256 = dllHash }],
            Signature            = sig,
        };

        var pkgPath = Path.Combine(_packagesDir, $"{pluginId}-{version}.msopkg");
        using var ms      = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // manifest.json
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var s = manifestEntry.Open())
                s.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, CanonicalOpts)));

            // Test.dll
            if (includeDllInArchive)
            {
                var dllEntry = archive.CreateEntry("Test.dll");
                using var s  = dllEntry.Open();
                s.Write(corruptDll ? new byte[] { 0xFF, 0xFF } : dllContent);
            }
        }
        File.WriteAllBytes(pkgPath, ms.ToArray());
        return pkgPath;
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallAsync_ValidUnsignedPackage_DevMode_Succeeds()
    {
        var pkgPath  = BuildPackage();
        var installer = MakeInstaller();

        var result = await installer.InstallAsync(pkgPath, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PluginId.Should().Be("test.plugin");
        result.InstalledVersion.Should().Be("1.0.0");
        Directory.Exists(Path.Combine(_pluginsDir, "test.plugin")).Should().BeTrue();
    }

    [Fact]
    public async Task InstallAsync_ValidUnsignedPackage_DevMode_WritesV1PluginJson()
    {
        var pkgPath   = BuildPackage();
        var installer = MakeInstaller();

        await installer.InstallAsync(pkgPath, CancellationToken.None);

        var v1JsonPath = Path.Combine(_pluginsDir, "test.plugin", "plugin.json");
        File.Exists(v1JsonPath).Should().BeTrue("installer must write derived v1 plugin.json for PluginLoader");
    }

    [Fact]
    public async Task InstallAsync_UnsignedPackage_RequireSignedPackages_Fails()
    {
        var pkgPath   = BuildPackage();
        var sec       = new PluginSecurityOptions { RequireSignedPackages = true };
        var installer = MakeInstaller(secOptions: sec);

        var result = await installer.InstallAsync(pkgPath, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("SignatureVerification");
    }

    [Fact]
    public async Task InstallAsync_TamperedDll_HashMismatch_Fails()
    {
        var (_, correctHash) = MakeDll();
        // Use correct hash in manifest but corrupt DLL in archive
        var pkgPath   = BuildPackage(dllHash: correctHash, corruptDll: true);
        var installer = MakeInstaller();

        var result = await installer.InstallAsync(pkgPath, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("HashVerification");
    }

    [Fact]
    public async Task InstallAsync_InvalidSignature_Fails()
    {
        // Verifier always returns InvalidSignature
        var verifierMock = new Mock<IPluginSignatureVerifier>();
        verifierMock
            .Setup(v => v.Verify(It.IsAny<ManifestV2>(), It.IsAny<string>()))
            .Returns(SignatureVerificationResult.InvalidSignature("test-key-01"));

        var pkgPath = BuildPackage(sig: new ManifestSignatureBlock
        {
            Algorithm   = "RSA-PSS-SHA256",
            PublicKeyId = "test-key-01",
            Value       = "AAAA==",
        });
        var installer = MakeInstaller(verifier: verifierMock.Object);

        var result = await installer.InstallAsync(pkgPath, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("SignatureVerification");
    }

    [Fact]
    public async Task InstallAsync_IncompatibleSdkVersion_Fails()
    {
        var pkgPath   = BuildPackage(sdkConstraint: ">=99.0.0 <100.0.0");
        var installer = MakeInstaller();

        var result = await installer.InstallAsync(pkgPath, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("SdkVersionConstraint");
    }

    [Fact]
    public async Task InstallAsync_ArchiveTooManyFiles_Fails()
    {
        // Create archive with 201 entries
        var pkgPath = Path.Combine(_packagesDir, "oversize.msopkg");
        using (var ms      = new MemoryStream())
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < 201; i++)
            {
                var entry = archive.CreateEntry($"file{i}.dat");
                using var s = entry.Open();
                s.WriteByte(0);
            }
        }
        // We need to write bytes separately due to ZipArchive leaveOpen pattern
        var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < 201; i++)
            {
                var e = archive.CreateEntry($"file{i}.dat");
                using var s = e.Open();
                s.WriteByte(0);
            }
        }
        File.WriteAllBytes(pkgPath, bytes.ToArray());

        var installer = MakeInstaller();
        var result    = await installer.InstallAsync(pkgPath, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("ArchiveValidation");
    }

    [Fact]
    public async Task InstallAsync_MissingManifestJson_Fails()
    {
        // Archive with no manifest.json
        var pkgPath = Path.Combine(_packagesDir, "no-manifest.msopkg");
        var ms      = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = archive.CreateEntry("Test.dll");
            using var s = e.Open();
            s.WriteByte(0x4D);
        }
        File.WriteAllBytes(pkgPath, ms.ToArray());

        var installer = MakeInstaller();
        var result    = await installer.InstallAsync(pkgPath, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("ArchiveValidation");
    }

    [Fact]
    public async Task InstallAsync_Upgrade_ReplacesExistingPluginDirectory()
    {
        // Install v1.0.0
        var pkg1      = BuildPackage(version: "1.0.0");
        var installer = MakeInstaller();
        var r1        = await installer.InstallAsync(pkg1, CancellationToken.None);
        r1.Success.Should().BeTrue();

        // Install v2.0.0 of same plugin
        var (newDll, newHash) = (new byte[] { 0xAB, 0xCD }, string.Empty);
        newHash = Convert.ToHexString(SHA256.HashData(newDll)).ToLowerInvariant();
        var pkg2 = BuildPackage(version: "2.0.0", dllContent: newDll, dllHash: newHash);
        var r2   = await installer.InstallAsync(pkg2, CancellationToken.None);

        r2.Success.Should().BeTrue();
        r2.InstalledVersion.Should().Be("2.0.0");
        Directory.Exists(Path.Combine(_pluginsDir, "test.plugin")).Should().BeTrue();
    }

    [Fact]
    public async Task InstallAsync_StoreUpsertCalled_WithPluginIdAndVersion()
    {
        var pkgPath   = BuildPackage();
        var installer = MakeInstaller();

        await installer.InstallAsync(pkgPath, CancellationToken.None);

        _storeMock.Verify(s => s.UpsertAsync(
            It.Is<PluginRecord>(r =>
                r.PluginId      == "test.plugin" &&
                r.PluginVersion == "1.0.0"       &&
                r.IsPackageInstall == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InstallAsync_SignedPackage_StoreUpsertContainsSignedBy()
    {
        var sigBlock = new ManifestSignatureBlock
        {
            Algorithm   = "RSA-PSS-SHA256",
            PublicKeyId = "trusted-key-01",
            Value       = "AAAA==",
        };

        var verifierMock = new Mock<IPluginSignatureVerifier>();
        verifierMock
            .Setup(v => v.Verify(It.IsAny<ManifestV2>(), It.IsAny<string>()))
            .Returns(SignatureVerificationResult.Valid("trusted-key-01"));

        var pkgPath   = BuildPackage(sig: sigBlock);
        var installer = MakeInstaller(verifier: verifierMock.Object);

        await installer.InstallAsync(pkgPath, CancellationToken.None);

        _storeMock.Verify(s => s.UpsertAsync(
            It.Is<PluginRecord>(r =>
                r.SignedBy           == "trusted-key-01" &&
                r.SignatureAlgorithm == "RSA-PSS-SHA256"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UninstallAsync_ExistingPlugin_RemovesDirectory()
    {
        // Pre-create a plugin directory
        var pluginDir = Path.Combine(_pluginsDir, "test.plugin");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), "{}");

        var installer = MakeInstaller();
        var result    = await installer.UninstallAsync("test.plugin", CancellationToken.None);

        result.Should().BeTrue();
        Directory.Exists(pluginDir).Should().BeFalse();
        _storeMock.Verify(s => s.SetEnabledAsync("test.plugin", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UninstallAsync_NonExistentPlugin_ReturnsFalse()
    {
        var installer = MakeInstaller();
        var result    = await installer.UninstallAsync("ghost.plugin", CancellationToken.None);
        result.Should().BeFalse();
    }
}
```

### Step 2 — Extend SyncPlugin entity

- [ ] Modify `src/MSOSync.Persistence/Entities/SyncPlugin.cs` — add the 4 new columns after `HostVersion`:

```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[GlobalEntity]
public sealed class SyncPlugin
{
    public string   PluginId           { get; set; } = null!;
    public string   PluginName         { get; set; } = null!;
    public string   PluginVersion      { get; set; } = null!;
    public string   Status             { get; set; } = null!;
    public bool     Enabled            { get; set; } = true;
    public DateTime InstalledAt        { get; set; }
    public DateTime LastSeenAt         { get; set; }
    public string?  LastError          { get; set; }
    public string?  ManifestHash       { get; set; }
    public string?  HostVersion        { get; set; }
    // 2C.1 additions:
    public string?  PackageHash        { get; set; }   // SHA-256 of the .msopkg file
    public string?  SignedBy           { get; set; }   // publicKeyId from signature block, null if unsigned
    public string?  SignatureAlgorithm { get; set; }   // "RSA-PSS-SHA256" or null if unsigned
    public bool     IsPackageInstall   { get; set; }   // true = installed via .msopkg, false = directory-based
}
```

### Step 3 — Extend PluginRecord model

- [ ] Modify `src/MSOSync.Plugin/Models/PluginRecord.cs` — add the 4 new properties after `HostVersion`:

```csharp
namespace MSOSync.Plugin.Models;

public sealed class PluginRecord
{
    public string   PluginId           { get; set; } = null!;
    public string   PluginName         { get; set; } = null!;
    public string   PluginVersion      { get; set; } = null!;
    public string   Status             { get; set; } = null!;   // PluginStatus enum name
    public bool     Enabled            { get; set; } = true;
    public DateTime InstalledAt        { get; set; }
    public DateTime LastSeenAt         { get; set; }
    public string?  LastError          { get; set; }
    public string?  ManifestHash       { get; set; }
    public string?  HostVersion        { get; set; }
    // 2C.1 additions:
    public string?  PackageHash        { get; set; }
    public string?  SignedBy           { get; set; }
    public string?  SignatureAlgorithm { get; set; }
    public bool     IsPackageInstall   { get; set; }
}
```

### Step 4 — Extend IPluginStore interface

- [ ] Modify `src/MSOSync.Plugin/Abstractions/IPluginStore.cs` — add `GetByIdAsync` and `DeleteAsync`:

```csharp
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Abstractions;

public interface IPluginStore
{
    Task<IReadOnlyList<PluginRecord>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PluginRecord record, CancellationToken ct);
    Task TouchAsync(string pluginId, CancellationToken ct);
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct);
    // 2C.1 additions:
    Task<PluginRecord?> GetByIdAsync(string pluginId, CancellationToken ct);
    Task DeleteAsync(string pluginId, CancellationToken ct);
}
```

### Step 5 — Create M036 migration

- [ ] Create `src/MSOSync.Persistence/Migrations/M036_PluginPackagingColumns.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M036_PluginPackagingColumns : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name:      "package_hash",
            schema:    Schema,
            table:     "sync_plugin",
            type:      "nvarchar(64)",
            maxLength: 64,
            nullable:  true);

        migrationBuilder.AddColumn<string>(
            name:      "signed_by",
            schema:    Schema,
            table:     "sync_plugin",
            type:      "nvarchar(200)",
            maxLength: 200,
            nullable:  true);

        migrationBuilder.AddColumn<string>(
            name:      "signature_algorithm",
            schema:    Schema,
            table:     "sync_plugin",
            type:      "nvarchar(50)",
            maxLength: 50,
            nullable:  true);

        migrationBuilder.AddColumn<bool>(
            name:         "is_package_install",
            schema:       Schema,
            table:        "sync_plugin",
            type:         "bit",
            nullable:     false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "package_hash",        schema: Schema, table: "sync_plugin");
        migrationBuilder.DropColumn(name: "signed_by",           schema: Schema, table: "sync_plugin");
        migrationBuilder.DropColumn(name: "signature_algorithm", schema: Schema, table: "sync_plugin");
        migrationBuilder.DropColumn(name: "is_package_install",  schema: Schema, table: "sync_plugin");
    }
}
```

**Note:** After creating this file, run `dotnet ef migrations add M036_PluginPackagingColumns` is NOT required here because this is a hand-crafted migration. However, the EF Core model snapshot (`AppDbContextModelSnapshot.cs`) must be updated to include the new columns in the `SyncPlugin` entity configuration. If the snapshot is auto-generated, regenerate it by running:

```powershell
dotnet ef migrations script --idempotent -p src/MSOSync.Persistence -s src/MSOSync.App 2>&1 | Select-Object -Last 10
```

This validates the migration is syntactically correct. Update the snapshot manually if auto-generation is not available. The snapshot update is required only for EF migrations tooling; the migration itself runs via `MigrationRunner` at startup and does not require snapshot synchronization at runtime.

### Step 6 — Implement PluginInstaller

- [ ] Create `src/MSOSync.Plugin/Packaging/Installer/PluginInstaller.cs`:

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging.Abstractions;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing.Abstractions;
using MSOSync.Plugin.Signing.Models;

namespace MSOSync.Plugin.Packaging.Installer;

public sealed class PluginInstaller(
    IPluginStore                    store,
    IPluginSignatureVerifier        verifier,
    IOptions<PluginSecurityOptions> securityOptions,
    IOptions<PluginHostOptions>     hostOptions,
    IOptions<PackagingOptions>      packagingOptions,
    ILogger<PluginInstaller>        logger) : IPluginInstaller
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions CanonicalOpts = new()
    {
        WriteIndented          = false,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions V1WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly EventId InstallStarted   = new(3001, "PluginInstall3001");
    private static readonly EventId RollbackAttempted = new(3002, "PluginInstall3002");
    private static readonly EventId InstallSucceeded  = new(3003, "PluginInstall3003");
    private static readonly EventId InstallFailed     = new(3004, "PluginInstall3004");
    private static readonly EventId PluginUninstalled = new(3005, "PluginInstall3005");
    private static readonly EventId UnsignedAccepted  = new(2002, "PluginSecurity2002");
    private static readonly EventId HashVerifyDone    = new(2003, "PluginSecurity2003");

    public async Task<PackageInstallResult> InstallAsync(string packagePath, CancellationToken ct)
    {
        var sec   = securityOptions.Value;
        var host  = hostOptions.Value;
        var pkgOpts = packagingOptions.Value;

        logger.Log(LogLevel.Information, InstallStarted,
            "Package installation started: '{PackagePath}'", packagePath);

        string pluginId = "?";
        string tempDir  = string.Empty;

        try
        {
            // ── Stage 1: Archive Validation ──────────────────────────────────────
            if (!File.Exists(packagePath))
                return Fail(pluginId, "ArchiveValidation", $"Package file not found: '{packagePath}'");

            ZipArchive zip;
            try
            {
                zip = ZipFile.OpenRead(packagePath);
            }
            catch (Exception ex)
            {
                return Fail(pluginId, "ArchiveValidation", $"Not a valid ZIP archive: {ex.Message}");
            }

            using (zip)
            {
                // file count limit
                if (zip.Entries.Count > pkgOpts.MaxFileCount)
                    return Fail(pluginId, "ArchiveValidation",
                        $"Archive exceeds maximum file count of {pkgOpts.MaxFileCount} (found {zip.Entries.Count}).");

                // uncompressed size limit
                var totalSize = zip.Entries.Sum(e => e.Length);
                if (totalSize > pkgOpts.MaxPackageSizeBytes)
                    return Fail(pluginId, "ArchiveValidation",
                        $"Archive exceeds maximum uncompressed size of {pkgOpts.MaxPackageSizeBytes} bytes.");

                // must contain manifest.json at root
                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry is null)
                    return Fail(pluginId, "ArchiveValidation",
                        "manifest.json not found at archive root.");

                // ── Stage 2: Manifest Parse ──────────────────────────────────────
                string rawManifestJson;
                ManifestV2 manifest;
                try
                {
                    using var ms = new MemoryStream((int)Math.Min(manifestEntry.Length, host.MaxManifestSizeBytes));
                    await using (var es = manifestEntry.Open())
                        await es.CopyToAsync(ms, ct);
                    rawManifestJson = Encoding.UTF8.GetString(ms.ToArray());
                    manifest = JsonSerializer.Deserialize<ManifestV2>(rawManifestJson, ReadOpts)
                               ?? throw new JsonException("Manifest deserialized to null.");
                }
                catch (JsonException ex)
                {
                    return Fail(pluginId, "ManifestParse", ex.Message);
                }

                pluginId = manifest.Id ?? "?";

                // ── Stage 3: Schema Validation ───────────────────────────────────
                var validationError = ManifestV2Validator.Validate(manifest);
                if (validationError is not null)
                    return Fail(pluginId, "ManifestValidation", validationError);

                // ── Stage 4: SDK Version Constraint ──────────────────────────────
                var hostSdkVersion = new Version(int.Parse(host.SupportedSdkMajorVersion), 0, 0);
                if (!SdkVersionConstraintParser.Satisfies(manifest.SdkVersionConstraint, hostSdkVersion))
                    return Fail(pluginId, "SdkVersionConstraint",
                        $"Host SDK version {hostSdkVersion} does not satisfy constraint '{manifest.SdkVersionConstraint}'.");

                // ── Stage 5: Signature Verification ──────────────────────────────
                var sigResult = verifier.Verify(manifest, rawManifestJson);
                if (manifest.Signature is not null)
                {
                    // Signature present but invalid → always fail
                    if (!sigResult.IsValid)
                        return Fail(pluginId, "SignatureVerification", sigResult.ErrorMessage ?? "Signature invalid.");
                }
                else
                {
                    // No signature block
                    if (sec.RequireSignedPackages)
                        return Fail(pluginId, "SignatureVerification",
                            "Package has no signature block and RequireSignedPackages = true.");
                    // Log dev-mode acceptance
                    logger.Log(LogLevel.Information, UnsignedAccepted,
                        "Unsigned package accepted for plugin '{PluginId}' (RequireSignedPackages = false).", pluginId);
                }

                // ── Stage 6: File Hash Verification ──────────────────────────────
                foreach (var fileEntry in manifest.Files)
                {
                    var archiveFile = zip.GetEntry(fileEntry.Path.Replace('\\', '/'));
                    if (archiveFile is null)
                        return Fail(pluginId, "HashVerification",
                            $"File '{fileEntry.Path}' listed in manifest.files[] not found in archive.");

                    var actualHash = await ComputeEntryHashAsync(archiveFile, ct);
                    if (!string.Equals(actualHash, fileEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                        return Fail(pluginId, "HashVerification",
                            $"Hash mismatch for '{fileEntry.Path}'. Expected '{fileEntry.Sha256}', computed '{actualHash}'.");
                }

                logger.Log(LogLevel.Debug, HashVerifyDone,
                    "Hash verification complete for plugin '{PluginId}': {Count} files verified.",
                    pluginId, manifest.Files.Count);

                // ── Stage 7: Unpack to Temp Directory ────────────────────────────
                tempDir = Path.Combine(
                    Path.GetTempPath(),
                    $"msopkg-{manifest.Id}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                var canonicalTemp = Path.GetFullPath(tempDir);
                int assetCount    = 0;
                long assetsSize   = 0;

                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

                    var destPath = Path.GetFullPath(Path.Combine(tempDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

                    // Path traversal guard
                    if (!destPath.StartsWith(canonicalTemp, StringComparison.OrdinalIgnoreCase))
                        return Fail(pluginId, "Unpack",
                            $"Path traversal detected: archive entry '{entry.FullName}' would escape the temp directory.");

                    if (entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        assetCount++;
                        assetsSize += entry.Length;
                        if (assetCount > pkgOpts.MaxAssetsFileCount)
                            return Fail(pluginId, "Unpack",
                                $"assets/ directory exceeds maximum file count of {pkgOpts.MaxAssetsFileCount}.");
                        if (assetsSize > pkgOpts.MaxAssetsSizeBytes)
                            return Fail(pluginId, "Unpack",
                                $"assets/ directory exceeds maximum size of {pkgOpts.MaxAssetsSizeBytes} bytes.");
                    }

                    var destDir = Path.GetDirectoryName(destPath)!;
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                    await using var entryStream = entry.Open();
                    await using var destStream  = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await entryStream.CopyToAsync(destStream, ct);
                }

                // ── Stage 8: Write derived v1 plugin.json ────────────────────────
                var v1Manifest = new PluginManifest
                {
                    ManifestVersion = 1,
                    Id              = manifest.Id,
                    Name            = manifest.Name,
                    Version         = manifest.Version,
                    SdkVersion      = manifest.SdkVersion,
                    ApiVersion      = manifest.ApiVersion,
                    StartupOrder    = manifest.StartupOrder,
                    MinHostVersion  = manifest.MinHostVersion,
                    MaxHostVersion  = manifest.MaxHostVersion,
                    EntryAssembly   = manifest.EntryAssembly,
                    EntryType       = manifest.EntryType,
                    Author          = manifest.Author,
                    Description     = manifest.Description,
                    Permissions     = manifest.Permissions,
                    Dependencies    = manifest.PluginDependencies.Select(d => d.Id).ToList(),
                    Capabilities    = manifest.Capabilities,
                };
                var v1Json = JsonSerializer.Serialize(v1Manifest, V1WriteOpts);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "plugin.json"), v1Json, ct);

                // ── Stage 9: Atomic Move ──────────────────────────────────────────
                var destination = Path.Combine(host.PluginsPath, manifest.Id);
                string? bakDir  = null;

                try
                {
                    if (Directory.Exists(destination))
                    {
                        bakDir = $"{destination}.bak.{DateTime.UtcNow:yyyyMMddHHmmss}";
                        Directory.Move(destination, bakDir);
                    }

                    if (!Directory.Exists(host.PluginsPath))
                        Directory.CreateDirectory(host.PluginsPath);

                    Directory.Move(tempDir, destination);
                    tempDir = string.Empty; // ownership transferred

                    if (bakDir is not null && Directory.Exists(bakDir))
                        Directory.Delete(bakDir, true);
                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.Warning, RollbackAttempted,
                        "AtomicMove failed for plugin '{PluginId}'. Attempting rollback. Error: {Error}",
                        pluginId, ex.Message);

                    // Attempt to restore .bak
                    if (bakDir is not null && Directory.Exists(bakDir))
                    {
                        try { Directory.Move(bakDir, destination); }
                        catch (Exception rbEx)
                        {
                            logger.LogWarning(rbEx, "Rollback restore failed for '{PluginId}'.", pluginId);
                        }
                    }

                    return Fail(pluginId, "AtomicMove", ex.Message);
                }

                // ── Stage 10: Persist to Store ────────────────────────────────────
                var packageHash = ComputeFileHash(packagePath);
                var record = new PluginRecord
                {
                    PluginId           = manifest.Id,
                    PluginName         = manifest.Name,
                    PluginVersion      = manifest.Version,
                    Status             = PluginStatus.Loaded.ToString(),
                    Enabled            = true,
                    InstalledAt        = DateTime.UtcNow,
                    LastSeenAt         = DateTime.UtcNow,
                    LastError          = null,
                    ManifestHash       = packageHash,
                    HostVersion        = host.HostVersion,
                    PackageHash        = packageHash,
                    SignedBy           = manifest.Signature?.PublicKeyId,
                    SignatureAlgorithm = manifest.Signature?.Algorithm,
                    IsPackageInstall   = true,
                };
                await store.UpsertAsync(record, ct);

                logger.Log(LogLevel.Information, InstallSucceeded,
                    "Plugin '{PluginId}' v{Version} installed successfully.", manifest.Id, manifest.Version);

                return PackageInstallResult.Ok(manifest.Id, manifest.Version);
            }
        }
        catch (Exception ex)
        {
            logger.Log(LogLevel.Warning, InstallFailed,
                "Plugin installation failed at unknown stage. Plugin: '{PluginId}'. Error: {Error}",
                pluginId, ex.Message);
            return Fail(pluginId, "Unknown", ex.Message);
        }
        finally
        {
            // Clean up temp dir if it still exists (failure path)
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up temp directory '{TempDir}'.", tempDir);
                }
            }
        }
    }

    public async Task<bool> UninstallAsync(string pluginId, CancellationToken ct)
    {
        var destination = Path.Combine(hostOptions.Value.PluginsPath, pluginId);
        if (!Directory.Exists(destination)) return false;

        Directory.Delete(destination, true);
        await store.SetEnabledAsync(pluginId, false, ct);

        logger.Log(LogLevel.Information, PluginUninstalled, "Plugin '{PluginId}' uninstalled.", pluginId);
        return true;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private PackageInstallResult Fail(string pluginId, string stage, string error)
    {
        logger.Log(LogLevel.Warning, InstallFailed,
            "Plugin installation failed. PluginId='{PluginId}', Stage='{Stage}', Error='{Error}'",
            pluginId, stage, error);
        return PackageInstallResult.Fail(pluginId, stage, error);
    }

    private static async Task<string> ComputeEntryHashAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        using var incHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer        = new byte[4096];
        await using var stream = entry.Open();
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            incHash.AppendData(buffer, 0, read);
        return Convert.ToHexString(incHash.GetCurrentHash()).ToLowerInvariant();
    }

    private static string ComputeFileHash(string filePath)
    {
        using var incHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer        = new byte[4096];
        using var stream  = File.OpenRead(filePath);
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            incHash.AppendData(buffer, 0, read);
        return Convert.ToHexString(incHash.GetCurrentHash()).ToLowerInvariant();
    }
}
```

### Step 7 — Find and update IPluginStore implementation in MSOSync.Persistence

The concrete implementation of `IPluginStore` is in `MSOSync.Persistence`. Search for it:

```powershell
Get-ChildItem -Recurse -Path "src\MSOSync.Persistence" -Filter "*.cs" | Select-String "IPluginStore" | Select-Object -ExpandProperty Filename
```

Add the two new methods (`GetByIdAsync` + `DeleteAsync`) to the concrete class. Both reads use `AsNoTracking()`. Pattern to follow:

```csharp
// GetByIdAsync
public async Task<PluginRecord?> GetByIdAsync(string pluginId, CancellationToken ct)
{
    var entity = await _db.SyncPlugins
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.PluginId == pluginId, ct);
    return entity is null ? null : MapToRecord(entity);
}

// DeleteAsync
public async Task DeleteAsync(string pluginId, CancellationToken ct)
{
    var entity = await _db.SyncPlugins
        .FirstOrDefaultAsync(p => p.PluginId == pluginId, ct);
    if (entity is not null)
    {
        _db.SyncPlugins.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
```

Also update the `MapToRecord` / `MapToEntity` helpers (or wherever the mapping is done) to include the 4 new fields:

```csharp
// In map-to-record:
PackageHash        = entity.PackageHash,
SignedBy           = entity.SignedBy,
SignatureAlgorithm = entity.SignatureAlgorithm,
IsPackageInstall   = entity.IsPackageInstall,

// In upsert / map-to-entity:
entity.PackageHash        = record.PackageHash;
entity.SignedBy           = record.SignedBy;
entity.SignatureAlgorithm = record.SignatureAlgorithm;
entity.IsPackageInstall   = record.IsPackageInstall;
```

**Note:** Locate the actual file path before editing. The concrete store is likely at `src/MSOSync.Persistence/Repositories/PluginStore.cs` or similar.

### Step 8 — Update EF Core DbContext for new columns

Locate `AppDbContext` (or equivalent) and add the column configuration for the 4 new `SyncPlugin` properties in `OnModelCreating` or through the convention-based configuration:

```csharp
// In entity configuration for SyncPlugin:
entity.Property(e => e.PackageHash)
    .HasMaxLength(64)
    .HasColumnName("package_hash");

entity.Property(e => e.SignedBy)
    .HasMaxLength(200)
    .HasColumnName("signed_by");

entity.Property(e => e.SignatureAlgorithm)
    .HasMaxLength(50)
    .HasColumnName("signature_algorithm");

entity.Property(e => e.IsPackageInstall)
    .HasColumnName("is_package_install")
    .HasDefaultValue(false);
```

**Note:** Locate the actual DbContext file before editing. Adjust column name conventions to match the existing pattern (snake_case `plugin_id`, `plugin_name`, etc.).

### Step 9 — Build and run unit tests

- [ ] Run: `dotnet build src/MSOSync.Plugin/MSOSync.Plugin.csproj -c Debug 2>&1 | Select-Object -Last 20`

  Expected: 0 errors.

- [ ] Run: `dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj -c Debug 2>&1 | Select-Object -Last 20`

  Expected: 0 errors.

- [ ] Run: `dotnet test tests/MSOSync.PluginTests/MSOSync.PluginTests.csproj --filter "FullyQualifiedName~PluginInstallerTests" -c Debug 2>&1 | Select-Object -Last 40`

  Expected: all tests pass.

### Step 10 — Commit

- [ ] Stage files:

```
git add src/MSOSync.Persistence/Entities/SyncPlugin.cs
git add src/MSOSync.Plugin/Models/PluginRecord.cs
git add src/MSOSync.Plugin/Abstractions/IPluginStore.cs
git add src/MSOSync.Persistence/Migrations/M036_PluginPackagingColumns.cs
git add src/MSOSync.Plugin/Packaging/Installer/PluginInstaller.cs
git add tests/MSOSync.PluginTests/Packaging/PluginInstallerTests.cs
```

Also stage any concrete store implementation changes:

```
git add src/MSOSync.Persistence/Repositories/PluginStore.cs   # or actual path
git add src/MSOSync.Persistence/AppDbContext.cs               # or actual path
```

- [ ] Commit:

```
git commit -m "feat(2C.1-T3): PluginInstaller, M036 migration, extend SyncPlugin/PluginRecord/IPluginStore"
```
