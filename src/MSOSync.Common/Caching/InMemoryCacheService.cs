using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MSOSync.Common.Caching;

internal sealed class InMemoryCacheService(
    IMemoryCache cache,
    IOptions<CacheOptions> options) : ICacheService
{
    private readonly CacheOptions _opts = options.Value;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var ttl = expiry ?? _opts.DefaultExpiry;
        cache.Set(key, value, ttl);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        => throw new NotSupportedException(
            "RemoveByPrefixAsync is not supported by the InMemory cache provider. " +
            "Switch to Provider=Redis or invalidate keys individually.");
}
