namespace MSOSync.Plugin.Models;

public sealed record PluginLoadResult(
    string             PluginId,
    PluginLoadOutcome  Outcome,
    string?            FailureStage,
    string?            ErrorMessage);
