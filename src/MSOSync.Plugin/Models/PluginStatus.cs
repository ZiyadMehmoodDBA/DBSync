namespace MSOSync.Plugin.Models;

public enum PluginStatus
{
    Loaded,       // Assembly loaded, awaiting lifecycle start
    Initialized,  // InitializeAsync completed
    Running,      // StartAsync completed — normal operation
    Stopped,      // StopAsync completed
    Disabled,
    Failed
}

public enum PluginLoadOutcome { Success, Skipped, Disabled, Failed }
