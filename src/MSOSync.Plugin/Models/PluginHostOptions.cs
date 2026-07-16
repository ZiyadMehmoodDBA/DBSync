namespace MSOSync.Plugin.Models;

public sealed class PluginHostOptions
{
    public string PluginsPath              { get; set; } = "plugins";
    public string HostVersion              { get; set; } = "1.0.0";
    public string SupportedSdkMajorVersion { get; set; } = "1";
    public string SupportedApiVersion      { get; set; } = "1";
    public long   MaxPluginConfigSizeBytes { get; set; } = 1_048_576;
}
