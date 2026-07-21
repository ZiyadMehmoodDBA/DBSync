namespace MSOSync.Api.Dtos.Plugins;

public sealed class PluginSummaryDto
{
    public int       Total             { get; init; }
    public int       Loaded            { get; init; }
    public int       Failed            { get; init; }
    public int       Disabled          { get; init; }
    public long      StartupDurationMs { get; init; }
    public DateTime? LastScanAt        { get; init; }
}
