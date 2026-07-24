namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceUpdateManifestDto(
    string   PluginId,
    string   InstalledVersion,
    string   AvailableVersion,
    string   DownloadUrl,
    string   Sha256,
    string?  ReleaseNotes,
    DateTime PublishedAt);
