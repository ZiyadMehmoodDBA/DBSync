namespace MSOSync.Sdk.Metadata;

[Flags]
public enum PluginCapability
{
    None      = 0,
    Collector = 1,
    Transport = 2,
    Operation = 4,
    Router    = 8,
    Health    = 16
}
