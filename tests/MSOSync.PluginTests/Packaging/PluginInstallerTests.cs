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

    // ── helpers ────────────────────────────────────────────────────────────────

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

    /// <summary>Builds a minimal in-memory .msopkg archive and writes it to disk.</summary>
    private string BuildPackage(
        string pluginId              = "test.plugin",
        string version               = "1.0.0",
        string sdkConstraint         = ">=1.0.0 <2.0.0",
        byte[]? dllContent           = null,
        string? dllHash              = null,
        ManifestSignatureBlock? sig  = null,
        bool includeDllInArchive     = true,
        bool corruptDll              = false)
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

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallAsync_ValidUnsignedPackage_DevMode_Succeeds()
    {
        var pkgPath   = BuildPackage();
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
        var pkgPath = Path.Combine(_packagesDir, "oversize.msopkg");
        var bytes   = new MemoryStream();
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
        var newDll  = new byte[] { 0xAB, 0xCD };
        var newHash = Convert.ToHexString(SHA256.HashData(newDll)).ToLowerInvariant();
        var pkg2    = BuildPackage(version: "2.0.0", dllContent: newDll, dllHash: newHash);
        var r2      = await installer.InstallAsync(pkg2, CancellationToken.None);

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
                r.PluginId        == "test.plugin" &&
                r.PluginVersion   == "1.0.0"       &&
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
