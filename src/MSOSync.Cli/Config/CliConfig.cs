namespace MSOSync.Cli.Config;

public sealed record CliConfig
{
    public string ServerUrl      { get; init; } = "http://localhost:5000";
    public string ServerToken    { get; init; } = string.Empty;
    public string RegistryUrl    { get; init; } = "https://marketplace.msosync.io";
    public string RegistryApiKey { get; init; } = string.Empty;
    public string SigningKeyPath { get; init; } = string.Empty;
}
