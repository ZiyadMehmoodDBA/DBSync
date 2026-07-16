namespace MSOSync.Plugin.Models;

public sealed class PluginHostOptions
{
    public string PluginsPath              { get; set; } = "plugins";
    public string HostVersion              { get; set; } = "1.0.0";
    public int    DefaultTimeoutSeconds    { get; set; } = 30;
    public int?   InitializeTimeoutSeconds { get; set; }
    public int?   StartTimeoutSeconds      { get; set; }
    public int?   StopTimeoutSeconds       { get; set; }
    public int?   DisposeTimeoutSeconds    { get; set; }
    public string SupportedSdkMajorVersion { get; set; } = "1";
    public string SupportedApiVersion      { get; set; } = "1";
    public long   MaxPluginConfigSizeBytes { get; set; } = 1_048_576;
    public int    MaxPluginCount           { get; set; } = 100;
    public long   MaxManifestSizeBytes     { get; set; } = 65_536;
}
