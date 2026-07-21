namespace MSOSync.Api.Dtos.Plugins;

public sealed class PluginDto
{
    public string    PluginId             { get; init; } = null!;
    public string    Name                 { get; init; } = null!;
    public string    Version              { get; init; } = null!;
    public string    Status               { get; init; } = null!;
    public long      LoadDurationMs       { get; init; }
    public long?     InitializeDurationMs { get; init; }
    public long?     StartDurationMs      { get; init; }
    public long?     TotalDurationMs      { get; init; }
    public DateTime  LoadedAt             { get; init; }
    public DateTime? InitializedAt        { get; init; }
    public DateTime? StartedAt            { get; init; }
    public string?   LastError            { get; init; }
    public string?   FailureStage         { get; init; }
    public string    HostCompatibility    { get; init; } = null!;
    public IReadOnlyList<string> Capabilities  { get; init; } = [];
    public IReadOnlyList<string> Permissions   { get; init; } = [];
    public IReadOnlyList<string> Dependencies  { get; init; } = [];
}
