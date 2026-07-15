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
