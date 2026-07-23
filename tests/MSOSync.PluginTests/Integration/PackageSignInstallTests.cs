using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging.Installer;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Packaging.Packager;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing;
using MSOSync.Plugin.Signing.Models;
using Xunit;

namespace MSOSync.PluginTests.Integration;

/// <summary>
/// Integration tests: pack → (optionally sign) → install → verify store + filesystem.
/// Uses real ZIP archives (on-disk), real PluginPackager, real PluginInstaller,
/// real RsaPssSignatureVerifier, and a mock IPluginStore.
/// </summary>
public sealed class PackageSignInstallTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _outputDir;
    private readonly string _pluginsDir;

    public PackageSignInstallTests()
    {
        var id      = Guid.NewGuid().ToString("N");
        _sourceDir  = Path.Combine(Path.GetTempPath(), $"psit-src-{id}");
        _outputDir  = Path.Combine(Path.GetTempPath(), $"psit-out-{id}");
        _pluginsDir = Path.Combine(Path.GetTempPath(), $"psit-plugins-{id}");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_outputDir);
        Directory.CreateDirectory(_pluginsDir);
    }

    public void Dispose()
    {
        foreach (var d in new[] { _sourceDir, _outputDir, _pluginsDir })
            if (Directory.Exists(d)) try { Directory.Delete(d, true); } catch { /* best-effort */ }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void WriteMinimalSource(
        string pluginId  = "integration.test.plugin",
        string version   = "1.0.0",
        string dllName   = "IntegrationTest.dll")
    {
        // Write a stub DLL (MZ header)
        File.WriteAllBytes(Path.Combine(_sourceDir, dllName), new byte[] { 0x4D, 0x5A, 0x00, 0x00 });

        var manifest = new
        {
            manifestVersion      = 2,
            id                   = pluginId,
            name                 = "Integration Test Plugin",
            version              = version,
            sdkVersion           = "1.0",
            sdkVersionConstraint = ">=1.0.0 <2.0.0",
            apiVersion           = "1",
            minHostVersion       = "1.0.0",
            maxHostVersion       = "99.0.0",
            entryAssembly        = dllName,
            entryType            = "IntegrationTest.Plugin",
            author               = "Test",
            description          = "Integration test plugin.",
        };
        File.WriteAllText(
            Path.Combine(_sourceDir, "plugin.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private (PluginPackager packager, PluginInstaller installer, Mock<IPluginStore> storeMock)
        BuildPipeline(
            RsaPssPluginSigner?    signer     = null,
            PluginSecurityOptions? secOptions = null)
    {
        secOptions ??= new PluginSecurityOptions { RequireSignedPackages = false };

        var hostOpts = new PluginHostOptions
        {
            PluginsPath              = _pluginsDir,
            HostVersion              = "15.0.0",
            SupportedSdkMajorVersion = "1",
            SupportedApiVersion      = "1",
        };

        var storeMock = new Mock<IPluginStore>();
        storeMock.Setup(s => s.UpsertAsync(It.IsAny<PluginRecord>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        storeMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        storeMock.Setup(s => s.SetEnabledAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        // Build registry (empty for unsigned tests; populated for signed tests)
        TrustedPublisherRegistry registry;
        if (signer is not null)
        {
            // Export public key for the test RSA key
            var rsa       = GetRsaFromSigner(signer);
            var pubKeyB64 = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            var key       = new PluginSigningKey
            {
                KeyId        = signer.PublicKeyId,
                Publisher    = "Test Publisher",
                PublicKeyB64 = pubKeyB64,
                AddedAt      = "2024-01-01T00:00:00Z",
            };
            registry = new TrustedPublisherRegistry(
                Options.Create(secOptions),
                NullLogger<TrustedPublisherRegistry>.Instance,
                [key]);
        }
        else
        {
            registry = new TrustedPublisherRegistry(
                Options.Create(secOptions),
                NullLogger<TrustedPublisherRegistry>.Instance,
                []);
        }

        var verifier = new RsaPssSignatureVerifier(
            registry,
            Options.Create(secOptions),
            NullLogger<RsaPssSignatureVerifier>.Instance);

        var packager = new PluginPackager(
            Options.Create(new PackagingOptions()),
            Options.Create(hostOpts),
            NullLogger<PluginPackager>.Instance);

        var installer = new PluginInstaller(
            storeMock.Object,
            verifier,
            Options.Create(secOptions),
            Options.Create(hostOpts),
            Options.Create(new PackagingOptions()),
            NullLogger<PluginInstaller>.Instance);

        return (packager, installer, storeMock);
    }

    // Reflection to extract the internal RSA key from a signer (test-only)
    private static RSA GetRsaFromSigner(RsaPssPluginSigner signer)
    {
        var field = typeof(RsaPssPluginSigner)
            .GetField("_rsa", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (RSA)field.GetValue(signer)!;
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_UnsignedPackage_DevMode_PluginDirAndV1JsonPresent()
    {
        WriteMinimalSource();
        var (packager, installer, _) = BuildPipeline();
        var output = Path.Combine(_outputDir, "integration.test.plugin.msopkg");

        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);
        File.Exists(output).Should().BeTrue();

        var result = await installer.InstallAsync(output, CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage ?? "install failed unexpectedly");
        Directory.Exists(Path.Combine(_pluginsDir, "integration.test.plugin")).Should().BeTrue();
        File.Exists(Path.Combine(_pluginsDir, "integration.test.plugin", "plugin.json")).Should().BeTrue();
    }

    [Fact]
    public async Task RoundTrip_SignedPackage_ValidPublisher_StoreContainsSignedBy()
    {
        WriteMinimalSource();

        using var rsa  = RSA.Create(2048);
        var signer     = new RsaPssPluginSigner(rsa, "test-key-roundtrip");
        var secOptions = new PluginSecurityOptions { RequireSignedPackages = true, RequireTrustedPublisher = true };
        var (packager, installer, storeMock) = BuildPipeline(signer, secOptions);

        var output = Path.Combine(_outputDir, "signed.msopkg");
        await packager.PackageAsync(_sourceDir, output, signer, CancellationToken.None);
        var result = await installer.InstallAsync(output, CancellationToken.None);

        result.Success.Should().BeTrue(result.ErrorMessage ?? "signed install failed");
        storeMock.Verify(s => s.UpsertAsync(
            It.Is<PluginRecord>(r =>
                r.SignedBy           == "test-key-roundtrip" &&
                r.SignatureAlgorithm == "RSA-PSS-SHA256"     &&
                r.IsPackageInstall   == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TamperDetection_ModifiedDllAfterPacking_HashVerificationFails()
    {
        WriteMinimalSource();
        var (packager, installer, _) = BuildPipeline();
        var output = Path.Combine(_outputDir, "tampered.msopkg");

        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);

        // Re-open the archive and corrupt the DLL bytes
        var tampered = Path.Combine(_outputDir, "tampered-modified.msopkg");
        using (var srcZip = ZipFile.OpenRead(output))
        using (var dstMs  = new MemoryStream())
        {
            using (var dstZip = new ZipArchive(dstMs, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in srcZip.Entries)
                {
                    var newEntry = dstZip.CreateEntry(entry.FullName);
                    await using var src = entry.Open();
                    await using var dst = newEntry.Open();

                    if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        // Write corrupted bytes
                        await dst.WriteAsync(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
                    }
                    else
                    {
                        await src.CopyToAsync(dst);
                    }
                }
            }
            await File.WriteAllBytesAsync(tampered, dstMs.ToArray());
        }

        var result = await installer.InstallAsync(tampered, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("HashVerification");
    }

    [Fact]
    public async Task TamperDetection_SignatureInvalid_SignatureVerificationFails()
    {
        WriteMinimalSource();

        using var rsa  = RSA.Create(2048);
        var signer     = new RsaPssPluginSigner(rsa, "test-key-tamper");
        var secOptions = new PluginSecurityOptions { RequireSignedPackages = true, RequireTrustedPublisher = true };
        var (packager, installer, _) = BuildPipeline(signer, secOptions);

        var output = Path.Combine(_outputDir, "signed-tampered.msopkg");
        await packager.PackageAsync(_sourceDir, output, signer, CancellationToken.None);

        // Corrupt the signature value in manifest.json without changing files[]
        // so hash verification would pass but signature verification fails
        var tampered = Path.Combine(_outputDir, "signed-tampered-sig.msopkg");
        using (var srcZip = ZipFile.OpenRead(output))
        using (var dstMs  = new MemoryStream())
        {
            using (var dstZip = new ZipArchive(dstMs, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in srcZip.Entries)
                {
                    var newEntry = dstZip.CreateEntry(entry.FullName);
                    await using var src = entry.Open();
                    await using var dst = newEntry.Open();

                    if (entry.FullName == "manifest.json")
                    {
                        using var reader = new StreamReader(src);
                        var json         = await reader.ReadToEndAsync();
                        // Replace the signature value with a known-bad Base64 value
                        var corrupted = json.Replace(
                            "\"value\":",
                            "\"value\": \"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==\", \"value_ORIG\":");
                        await dst.WriteAsync(Encoding.UTF8.GetBytes(corrupted));
                    }
                    else
                    {
                        await src.CopyToAsync(dst);
                    }
                }
            }
            await File.WriteAllBytesAsync(tampered, dstMs.ToArray());
        }

        var result = await installer.InstallAsync(tampered, CancellationToken.None);

        result.Success.Should().BeFalse();
        // Fails at ManifestParse (invalid JSON due to duplicate key) or SignatureVerification
        result.FailureStage.Should().BeOneOf("ManifestParse", "SignatureVerification");
    }

    [Fact]
    public async Task UnsignedPackage_RequiredMode_InstallFails()
    {
        WriteMinimalSource();
        var (packager, installer, _) = BuildPipeline(
            secOptions: new PluginSecurityOptions { RequireSignedPackages = true });

        var output = Path.Combine(_outputDir, "unsigned-required.msopkg");
        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);
        var result = await installer.InstallAsync(output, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureStage.Should().Be("SignatureVerification");
    }

    [Fact]
    public async Task UnsignedPackage_DevMode_InstallSucceeds_StoreRecordUnsigned()
    {
        WriteMinimalSource();
        var (packager, installer, storeMock) = BuildPipeline(
            secOptions: new PluginSecurityOptions { RequireSignedPackages = false });

        var output = Path.Combine(_outputDir, "unsigned-dev.msopkg");
        await packager.PackageAsync(_sourceDir, output, null, CancellationToken.None);
        var result = await installer.InstallAsync(output, CancellationToken.None);

        result.Success.Should().BeTrue();
        // IsPackageInstall = true; SignedBy = null (unsigned)
        storeMock.Verify(s => s.UpsertAsync(
            It.Is<PluginRecord>(r =>
                r.IsPackageInstall == true &&
                r.SignedBy         == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upgrade_InstallSamePluginTwice_SecondVersionWins()
    {
        WriteMinimalSource(version: "1.0.0");
        var (packager1, installer, storeMock) = BuildPipeline();
        var out1 = Path.Combine(_outputDir, "v1.msopkg");
        await packager1.PackageAsync(_sourceDir, out1, null, CancellationToken.None);
        var r1 = await installer.InstallAsync(out1, CancellationToken.None);
        r1.Success.Should().BeTrue();

        // Update source to v2.0.0
        var dllV2 = Path.Combine(_sourceDir, "IntegrationTest.dll");
        File.WriteAllBytes(dllV2, new byte[] { 0xAB, 0xCD, 0xEF, 0x00 });

        var manifestV2 = new
        {
            manifestVersion      = 2,
            id                   = "integration.test.plugin",
            name                 = "Integration Test Plugin",
            version              = "2.0.0",
            sdkVersion           = "1.0",
            sdkVersionConstraint = ">=1.0.0 <2.0.0",
            apiVersion           = "1",
            minHostVersion       = "1.0.0",
            maxHostVersion       = "99.0.0",
            entryAssembly        = "IntegrationTest.dll",
            entryType            = "IntegrationTest.Plugin",
            author               = "Test",
            description          = "Integration test plugin v2.",
        };
        File.WriteAllText(
            Path.Combine(_sourceDir, "plugin.json"),
            JsonSerializer.Serialize(manifestV2));

        var out2 = Path.Combine(_outputDir, "v2.msopkg");
        await packager1.PackageAsync(_sourceDir, out2, null, CancellationToken.None);
        var r2 = await installer.InstallAsync(out2, CancellationToken.None);

        r2.Success.Should().BeTrue();
        r2.InstalledVersion.Should().Be("2.0.0");

        storeMock.Verify(s => s.UpsertAsync(
            It.Is<PluginRecord>(r => r.PluginVersion == "2.0.0"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
