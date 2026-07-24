namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplacePluginDetailDto(
    string                Id,
    string                Name,
    string                Author,
    string                Description,
    string                Category,
    IReadOnlyList<string> Tags,
    string                LatestVersion,
    string                MinHostVersion,
    long                  DownloadCount,
    double                Rating,
    int                   RatingCount,
    DateTime              PublishedAt,
    DateTime              UpdatedAt,
    string?               IconUrl,
    string?               ProjectUrl,
    string?               LicenseId,
    bool                  Verified,
    IReadOnlyList<MarketplaceVersionDto> Versions);
