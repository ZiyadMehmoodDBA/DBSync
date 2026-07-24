namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceSearchParams
{
    public string? Query    { get; init; }
    public string? Category { get; init; }
    public int     Page     { get; init; } = 1;
    public int     PageSize { get; init; } = 20;
}
