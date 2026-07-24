namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceVersionDto(
    string   Version,
    string   MinHostVersion,
    string   MaxHostVersion,
    DateTime PublishedAt,
    string   DownloadUrl,
    string   Sha256,
    string?  ReleaseNotes,
    bool     Deprecated);
