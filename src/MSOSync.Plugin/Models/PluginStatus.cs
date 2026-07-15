namespace MSOSync.Plugin.Models;

public enum PluginStatus { Discovered, Validated, Loaded, Disabled, Failed }

public enum PluginLoadOutcome { Success, Skipped, Disabled, Failed }
