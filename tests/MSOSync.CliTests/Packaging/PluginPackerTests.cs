using System.IO.Compression;
using System.Text.Json;
using MSOSync.Cli.Packaging;
using Xunit;

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
