namespace MSOSync.Plugin.Packaging.Models;

public sealed record PackageInstallResult(
    bool    Success,
    string  PluginId,
    string? InstalledVersion,
    string? FailureStage,
    string? ErrorMessage)
{
    public static PackageInstallResult Ok(string pluginId, string version)
        => new(true, pluginId, version, null, null);

    public static PackageInstallResult Fail(string pluginId, string stage, string error)
        => new(false, pluginId, null, stage, error);
}
