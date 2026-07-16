using Microsoft.Extensions.Logging;

namespace MSOSync.Plugin.Loading;

public static class PluginLogEvents
{
    public static readonly EventId PluginDirectoryDiscovered = new(1001, "PluginDirectoryDiscovered");
    public static readonly EventId PluginLoaded              = new(1002, "PluginLoaded");
    public static readonly EventId PluginFailed              = new(1003, "PluginFailed");
    public static readonly EventId PluginDisabled            = new(1004, "PluginDisabled");
    public static readonly EventId PluginStartupSummary      = new(1005, "PluginStartupSummary");
    public static readonly EventId PluginInitialized         = new(1006, "PluginInitialized");
    public static readonly EventId PluginStarted             = new(1007, "PluginStarted");
    public static readonly EventId PluginStopped             = new(1008, "PluginStopped");
    public static readonly EventId PluginTimeout             = new(1009, "PluginTimeout");
    public static readonly EventId PluginDisposed            = new(1010, "PluginDisposed");
}
