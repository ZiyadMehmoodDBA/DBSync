namespace MSOSync.Plugin.Lifecycle;

internal enum PluginRuntimeState
{
    Loaded,       // 14A end state: assembly loaded, type verified
    Initializing, // InitializeAsync in progress
    Initialized,  // InitializeAsync completed
    Starting,     // StartAsync in progress
    Running,      // StartAsync completed — steady state
    Stopping,     // StopAsync in progress
    Stopped,      // StopAsync completed (always host-initiated)
    Disposing,    // DisposeAsync in progress
    Disposed,     // DisposeAsync completed
    Failed,       // any phase failed — LastException set
    Disabled      // filtered at stage 4 — never receives lifecycle calls
}
