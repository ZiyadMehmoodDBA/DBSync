using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MSOSync.Common.Caching;

internal sealed class RedisCacheService(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly CacheOptions _opts = options.Value;
    private IDatabase Db => redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var raw = await Db.StringGetAsync(key).ConfigureAwait(false);
            if (!raw.HasValue) return default;
            return JsonSerializer.Deserialize<T>(raw!, _json);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis GET failed for key {Key}; returning cache miss", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var ttl = expiry ?? _opts.DefaultExpiry;
        try
        {
            var json = JsonSerializer.Serialize(value, _json);
            await Db.StringSetAsync(key, json, ttl).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis SET failed for key {Key}; value not cached", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await Db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis DEL failed for key {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var server = redis.GetServers().FirstOrDefault()
                ?? throw new InvalidOperationException("No Redis server endpoints available.");

            var pattern = $"{prefix}*";
            var keys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
                keys.Add(key);

            if (keys.Count > 0)
                await Db.KeyDeleteAsync(keys.ToArray()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis prefix scan/DEL failed for prefix {Prefix}", prefix);
        }
    }
}
