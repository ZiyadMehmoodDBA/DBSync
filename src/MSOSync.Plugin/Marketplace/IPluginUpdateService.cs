using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Plugin.Marketplace;

/// <summary>
/// Compares locally installed plugin versions against the remote registry.
/// Registered as Scoped.
/// </summary>
public interface IPluginUpdateService
{
    /// <summary>
    /// Checks a single installed plugin for an available update.
    /// Returns null when the plugin is not in the registry or is already at latest.
    /// </summary>
    Task<PluginUpdateManifest?> CheckAsync(
        string pluginId,
        string installedVersion,
        CancellationToken ct);

    /// <summary>
    /// Checks all currently installed plugins for updates.
    /// Iterates IPluginStore.GetAllAsync and calls CheckAsync for each sequentially
    /// (no Task.WhenAll). Plugins not in the registry are silently skipped.
    /// </summary>
    Task<IReadOnlyList<PluginUpdateManifest>> CheckAllAsync(CancellationToken ct);
}
