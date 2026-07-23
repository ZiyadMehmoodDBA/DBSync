using MSOSync.Common.Locks;
using StackExchange.Redis;

namespace MSOSync.Persistence.Lock;

internal sealed class RedisDistributedLockService(
    IConnectionMultiplexer redis) : IDistributedLockService
{
    // Extend expiry only if caller is the current owner
    private static readonly string RenewScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then " +
        "    return redis.call('PEXPIRE', KEYS[1], ARGV[2]) " +
        "else " +
        "    return 0 " +
        "end";

    // Delete key only if caller is the current owner (canonical Redlock release)
    private static readonly string ReleaseScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then " +
        "    return redis.call('DEL', KEYS[1]) " +
        "else " +
        "    return 0 " +
        "end";

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var ok = await db.StringSetAsync(resource, owner, expiry, keepTtl: false, When.NotExists);

        if (!ok) return null;

        return new RedisDistributedLock(this, resource, owner, DateTimeOffset.UtcNow.Add(expiry));
    }

    public async Task<bool> RenewAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var result = (long)await db.ScriptEvaluateAsync(
            RenewScript,
            new RedisKey[]   { resource },
            new RedisValue[] { owner, (long)expiry.TotalMilliseconds });

        return result == 1;
    }

    public async Task ReleaseAsync(
        string resource, string owner, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.ScriptEvaluateAsync(
            ReleaseScript,
            new RedisKey[]   { resource },
            new RedisValue[] { owner });
    }

    public async Task<bool> IsHeldAsync(
        string resource, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        return await db.KeyExistsAsync(resource);
    }
}
