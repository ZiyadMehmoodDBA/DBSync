namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceInstallRequest
{
    /// <summary>Specific version to install. When null, installs the latest version.</summary>
    public string? Version { get; init; }
}
