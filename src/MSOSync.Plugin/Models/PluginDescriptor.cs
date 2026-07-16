namespace MSOSync.Plugin.Models;

public sealed record PluginDescriptor
{
    public string       PluginId          { get; init; } = null!;
    public string       Name              { get; init; } = null!;
    public string       Version           { get; init; } = null!;
    public PluginStatus Status            { get; set; }           // mutable for UpdateStatus
    public string?      ErrorMessage      { get; set; }           // mutable for UpdateStatus
    public string?      FailureStage      { get; init; }
    public DateTime     LoadedAt          { get; init; }
    public long         LoadDurationMs    { get; init; }
    public string       HostCompatibility { get; init; } = "Compatible";
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
    public PluginManifest? Manifest        { get; init; }
    public int StartupOrder                { get; init; } = 1000;
}
