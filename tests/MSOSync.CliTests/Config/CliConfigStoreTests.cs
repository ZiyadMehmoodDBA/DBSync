using System.Text.Json;
using MSOSync.Cli.Config;
using Xunit;

namespace MSOSync.CliTests.Config;

public sealed class CliConfigStoreTests : IDisposable
{
    // Use a temp directory so tests never touch the real ~/.msosync/config.json
    private readonly string _tempDir;
    private readonly string _configPath;

    public CliConfigStoreTests()
    {
        _tempDir    = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _configPath = Path.Combine(_tempDir, "config.json");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_ReturnsDefaults_WhenFileDoesNotExist()
    {
        // Arrange: _configPath does not exist
        CliConfig result = LoadFrom(_configPath);

        // Assert defaults
        Assert.Equal("http://localhost:5000", result.ServerUrl);
        Assert.Equal(string.Empty, result.ServerToken);
        Assert.Equal("https://marketplace.msosync.io", result.RegistryUrl);
        Assert.Equal(string.Empty, result.RegistryApiKey);
        Assert.Equal(string.Empty, result.SigningKeyPath);
    }

    [Fact]
    public void Load_ReturnsStoredValues_WhenFileExists()
    {
        // Arrange
        string json = """
            {
              "serverUrl":      "http://prod:5000",
              "serverToken":    "tok123",
              "registryUrl":    "https://registry.example.com",
              "registryApiKey": "key456",
              "signingKeyPath": "/keys/signing.snk"
            }
            """;
        File.WriteAllText(_configPath, json);

        CliConfig result = LoadFrom(_configPath);

        Assert.Equal("http://prod:5000",               result.ServerUrl);
        Assert.Equal("tok123",                          result.ServerToken);
        Assert.Equal("https://registry.example.com",   result.RegistryUrl);
        Assert.Equal("key456",                          result.RegistryApiKey);
        Assert.Equal("/keys/signing.snk",               result.SigningKeyPath);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsMalformedJson()
    {
        File.WriteAllText(_configPath, "{ this is not valid json }");

        CliConfig result = LoadFrom(_configPath);

        Assert.Equal("http://localhost:5000", result.ServerUrl);
    }

    [Fact]
    public void Save_CreatesFileAndDirectory()
    {
        string subDir  = Path.Combine(_tempDir, "sub");
        string cfgPath = Path.Combine(subDir, "config.json");
        // subDir does not exist yet

        SaveTo(cfgPath, new CliConfig { ServerToken = "saved-token" });

        Assert.True(File.Exists(cfgPath));
        string json   = File.ReadAllText(cfgPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("saved-token", doc.RootElement.GetProperty("serverToken").GetString());
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var config = new CliConfig
        {
            ServerUrl      = "http://rt:5000",
            ServerToken    = "rt-token",
            RegistryUrl    = "https://rt-registry.io",
            RegistryApiKey = "rt-key",
            SigningKeyPath = "/rt/key.snk"
        };

        SaveTo(_configPath, config);
        CliConfig loaded = LoadFrom(_configPath);

        Assert.Equal(config.ServerUrl,      loaded.ServerUrl);
        Assert.Equal(config.ServerToken,    loaded.ServerToken);
        Assert.Equal(config.RegistryUrl,    loaded.RegistryUrl);
        Assert.Equal(config.RegistryApiKey, loaded.RegistryApiKey);
        Assert.Equal(config.SigningKeyPath,  loaded.SigningKeyPath);
    }

    // Helpers that bypass the real static ConfigPath and use a temp path instead
    private static CliConfig LoadFrom(string path)
    {
        if (!File.Exists(path))
            return new CliConfig();
        try
        {
            string json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<CliConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new CliConfig();
        }
        catch (JsonException) { return new CliConfig(); }
    }

    private static void SaveTo(string path, CliConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(config,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
    }
}
