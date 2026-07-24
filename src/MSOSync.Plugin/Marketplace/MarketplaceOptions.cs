namespace MSOSync.Plugin.Marketplace;

public sealed class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    /// <summary>
    /// Base URL of the remote registry.
    /// When null or empty, all marketplace endpoints return 503.
    /// </summary>
    public string? RegistryUrl { get; set; }

    /// <summary>
    /// Optional API key sent in the X-Api-Key header.
    /// Leave null for public registries.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Minutes to retain remote results in the local DB cache. Default: 60.</summary>
    public int CacheMinutes { get; set; } = 60;

    /// <summary>Minutes to retain search results in IMemoryCache. Default: 5.</summary>
    public int MemoryCacheMinutes { get; set; } = 5;

    /// <summary>HTTP timeout in seconds for registry calls. Default: 30.</summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>Polly retry attempts on transient HTTP failures. Default: 3.</summary>
    public int RetryCount { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(RegistryUrl);
}
