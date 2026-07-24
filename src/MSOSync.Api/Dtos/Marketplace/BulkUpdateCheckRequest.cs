namespace MSOSync.Api.Dtos.Marketplace;

/// <summary>Request body for POST /api/v1/marketplace/updates/check.</summary>
public sealed record BulkUpdateCheckRequest
{
    /// <summary>
    /// When true, only returns plugins that HAVE available updates.
    /// When false (default), the service includes all checked plugins in TotalChecked.
    /// </summary>
    public bool UpdatesOnly { get; init; } = false;
}
