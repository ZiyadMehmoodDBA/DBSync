using Microsoft.Extensions.Logging;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;

namespace MSOSync.Metadata.Marketplace;

public sealed class PluginUpdateService(
    IMarketplaceService          marketplaceService,
    IPluginStore                 pluginStore,
    ILogger<PluginUpdateService> logger) : IPluginUpdateService
{
    public async Task<PluginUpdateManifest?> CheckAsync(
        string pluginId, string installedVersion, CancellationToken ct)
    {
        var latestEntry = await marketplaceService.GetLatestUpdateAsync(
            pluginId, installedVersion, ct);

        if (latestEntry is null) return null;

        return new PluginUpdateManifest(
            pluginId,
            installedVersion,
            latestEntry.Version,
            latestEntry.DownloadUrl,
            latestEntry.Sha256,
            latestEntry.ReleaseNotes,
            latestEntry.PublishedAt);
    }

    public async Task<IReadOnlyList<PluginUpdateManifest>> CheckAllAsync(CancellationToken ct)
    {
        var installed = await pluginStore.GetAllAsync(ct);
        var results   = new List<PluginUpdateManifest>(installed.Count);

        // Sequential — no Task.WhenAll (would saturate registry HTTP or share DbContext)
        foreach (var record in installed)
        {
            var manifest = await CheckAsync(record.PluginId, record.PluginVersion, ct);
            if (manifest is not null)
                results.Add(manifest);
        }

        logger.Log(LogLevel.Information, MarketplaceLogEvents.BulkUpdateChecked,
            "Bulk update check completed. TotalChecked: {Total}, UpdatesFound: {Found}",
            installed.Count, results.Count);

        return results;
    }
}
