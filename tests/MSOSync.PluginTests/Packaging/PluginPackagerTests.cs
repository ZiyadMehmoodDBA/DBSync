using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Packaging;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Packaging.Packager;
using MSOSync.Plugin.Signing.Abstractions;
using Xunit;

namespace MSOSync.PluginTests.Packaging;

/// <summary>Simple stub — Moq cannot mock ReadOnlySpan&lt;byte&gt; parameters.</summary>
internal sealed class FakePluginSigner(string publicKeyId, string returnValue) : IPluginSigner
{
    public string PublicKeyId => publicKeyId;
    public string Sign(ReadOnlySpan<byte> data) => returnValue;
}

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

        var fakeSigner = new FakePluginSigner("test-key-01", "AAAA==");

        var packager = MakePackager();
        var output   = Path.Combine(_outputDir, "test.plugin.msopkg");

        await packager.PackageAsync(_sourceDir, output, fakeSigner, CancellationToken.None);

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
