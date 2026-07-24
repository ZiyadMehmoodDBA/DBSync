namespace MSOSync.Api.Dtos.Marketplace;

public sealed record MarketplaceInstallResult(
    bool    Success,
    string  PluginId,
    string? InstalledVersion,
    bool    RestartRequired,
    string? ErrorMessage);
