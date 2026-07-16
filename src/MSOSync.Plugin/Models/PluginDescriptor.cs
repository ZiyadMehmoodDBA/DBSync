namespace MSOSync.Plugin.Models;

public sealed record PluginDescriptor
{
    public string       PluginId          { get; init; } = null!;
    public string       Name              { get; init; } = null!;
    public string       Version           { get; init; } = null!;
    public PluginStatus Status            { get; set; }
    public string?      ErrorMessage      { get; set; }
    public string?      FailureStage      { get; init; }
    public int          StartupOrder      { get; init; } = 1000;
    public DateTime     LoadedAt          { get; init; }
    public long         LoadDurationMs    { get; init; }
    public string       HostCompatibility { get; init; } = "Compatible";
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
    public PluginManifest? Manifest        { get; init; }

    // 14B lifecycle metrics (set by PluginLifecycleManager after each phase)
    public long?     InitializeDurationMs { get; set; }
    public long?     StartDurationMs      { get; set; }
    public long?     TotalDurationMs      { get; set; }
    public DateTime? InitializedAt        { get; set; }
    public DateTime? StartedAt            { get; set; }
}
