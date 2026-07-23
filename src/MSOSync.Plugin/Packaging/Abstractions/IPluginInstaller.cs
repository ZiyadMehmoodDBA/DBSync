using MSOSync.Plugin.Packaging.Models;

namespace MSOSync.Plugin.Packaging.Abstractions;

/// <summary>
/// Installs a .msopkg archive into the configured plugins directory.
/// Implementation: MSOSync.Plugin.Packaging.Installer.PluginInstaller (Task 3).
/// </summary>
public interface IPluginInstaller
{
    /// <summary>
    /// Install (or upgrade) the plugin packaged in <paramref name="packagePath"/>.
    /// Never throws; all exceptions are caught and translated into a <see cref="PackageInstallResult.Fail"/> result.
    /// </summary>
    /// <param name="packagePath">Absolute path to the .msopkg file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PackageInstallResult> InstallAsync(string packagePath, CancellationToken ct);

    /// <summary>
    /// Remove an installed plugin by ID. Deletes plugins/{pluginId} directory and
    /// calls IPluginStore.SetEnabledAsync(pluginId, false).
    /// Returns false if the plugin directory does not exist.
    /// </summary>
    Task<bool> UninstallAsync(string pluginId, CancellationToken ct);
}
