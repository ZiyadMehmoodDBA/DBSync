namespace MSOSync.Sdk.Metadata;

public sealed record PluginMetadata
{
    public string PluginId     { get; init; } = null!;
    public string Name         { get; init; } = null!;
    public string Version      { get; init; } = null!;
    public string SdkVersion   { get; init; } = null!;
    public string ApiVersion   { get; init; } = null!;
    public string Author       { get; init; } = null!;
    public string Description  { get; init; } = null!;
    public IReadOnlySet<PluginCapability> Capabilities { get; init; } = new HashSet<PluginCapability>();
    public IReadOnlySet<PluginPermission> Permissions  { get; init; } = new HashSet<PluginPermission>();
}
