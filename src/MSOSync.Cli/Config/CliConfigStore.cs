using System.Text.Json;

namespace MSOSync.Cli.Config;

public static class CliConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string ConfigPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".msosync",
            "config.json");

    /// <summary>
    /// Load config from disk. Returns default CliConfig if the file does not exist.
    /// Returns default CliConfig on malformed JSON (non-fatal).
    /// </summary>
    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new CliConfig();

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<CliConfig>(json, JsonOptions) ?? new CliConfig();
        }
        catch (JsonException)
        {
            return new CliConfig();
        }
    }

    /// <summary>
    /// Save config to disk. Creates ~/.msosync/ directory if needed.
    /// </summary>
    public static void Save(CliConfig config)
    {
        string dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
    }
}
