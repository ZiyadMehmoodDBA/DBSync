namespace MSOSync.Plugin.Marketplace.Models;

/// <summary>Describes an available update for an installed plugin.</summary>
public sealed record PluginUpdateManifest(
    string   PluginId,
    string   InstalledVersion,
    string   AvailableVersion,
    string   DownloadUrl,
    string   Sha256,
    string?  ReleaseNotes,
    DateTime PublishedAt);
