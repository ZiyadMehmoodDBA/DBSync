using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

/// <summary>
/// Local cache of remote marketplace registry entries.
/// One row per plugin ID per registry source.
/// </summary>
[GlobalEntity]
public sealed class SyncMarketplaceCache
{
    /// <summary>Surrogate PK (int identity for fast seek).</summary>
    public int Id { get; set; }

    /// <summary>Registry base URL (normalized, trailing slash stripped).</summary>
    public string RegistryUrl { get; set; } = null!;

    /// <summary>Plugin ID as returned by the registry.</summary>
    public string PluginId { get; set; } = null!;

    /// <summary>Latest version string from the registry at cache time.</summary>
    public string LatestVersion { get; set; } = null!;

    /// <summary>JSON-serialized RegistryPluginEntry — full metadata blob.</summary>
    public string MetadataJson { get; set; } = null!;

    /// <summary>UTC timestamp when this entry was written or refreshed.</summary>
    public DateTime CachedAt { get; set; }

    /// <summary>UTC timestamp after which this entry is stale.</summary>
    public DateTime ExpiresAt { get; set; }
}
