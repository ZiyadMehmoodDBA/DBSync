namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplacePluginListItemDto(
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
    bool                  Verified);
