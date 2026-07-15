using Microsoft.Extensions.Logging;

namespace MSOSync.Plugin.Loading;

public static class PluginLogEvents
{
    public static readonly EventId PluginDirectoryDiscovered = new(1001, "PluginDirectoryDiscovered");
    public static readonly EventId PluginLoaded              = new(1002, "PluginLoaded");
    public static readonly EventId PluginFailed              = new(1003, "PluginFailed");
    public static readonly EventId PluginDisabled            = new(1004, "PluginDisabled");
    public static readonly EventId PluginStartupSummary      = new(1005, "PluginStartupSummary");
}
