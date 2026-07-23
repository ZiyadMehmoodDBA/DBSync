namespace MSOSync.Common.Caching;

public interface ICacheService
{
    /// <summary>Returns the cached value, or default(T) if the key does not exist.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores a value under the specified key.
    /// If <paramref name="expiry"/> is null, <see cref="CacheOptions.DefaultExpiry"/> is used.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);

    /// <summary>Removes a single key. No-op if the key does not exist.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all keys whose string representation begins with <paramref name="prefix"/>.
    /// Memory provider: throws NotSupportedException.
    /// Redis provider: uses SCAN + DEL.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
