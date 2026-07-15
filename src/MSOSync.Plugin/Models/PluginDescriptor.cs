namespace MSOSync.Plugin.Models;

// Full implementation in Task 4. Stub allows IPluginRegistry to compile.
public sealed record PluginDescriptor
{
    public string       PluginId         { get; init; } = null!;
    public string       Name             { get; init; } = null!;
    public string       Version          { get; init; } = null!;
    public PluginStatus Status           { get; init; }
    public string?      ErrorMessage     { get; init; }
    public string?      FailureStage     { get; init; }
    public DateTime     LoadedAt         { get; init; }
    public long         LoadDurationMs   { get; init; }
    public string       HostCompatibility { get; init; } = "Compatible";
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
    public PluginManifest? Manifest      { get; init; }
}
