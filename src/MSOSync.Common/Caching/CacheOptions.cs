namespace MSOSync.Common.Caching;

public sealed class CacheOptions
{
    public const string Section = "Cache";

    /// <summary>"Memory" (default) or "Redis".</summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>
    /// StackExchange.Redis connection string.
    /// Required when Provider == "Redis". Ignored when Provider == "Memory".
    /// Example: "localhost:6379,password=secret,abortConnect=false"
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Default TTL applied when SetAsync is called with expiry == null.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(5);
}
