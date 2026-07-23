using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MSOSync.Common.Caching;

internal sealed class RedisCacheHealthCheck(IConnectionMultiplexer redis)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis ping failed.", ex);
        }
    }
}
