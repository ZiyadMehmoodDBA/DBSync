namespace MSOSync.Api.Dtos.Marketplace;

public sealed record BulkUpdateCheckResult(
    int                                      TotalChecked,
    int                                      UpdatesAvailable,
    IReadOnlyList<MarketplaceUpdateManifestDto> Updates);
