using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

public static class DistributedLockServiceExtensions
{
    public static IServiceCollection AddDistributedLocks(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<DistributedLockOptions>(
            configuration.GetSection(DistributedLockOptions.SectionName));

        var provider = configuration
            .GetSection(DistributedLockOptions.SectionName)["Provider"] ?? "Sql";

        if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            // IConnectionMultiplexer must already be registered by the caller
            // (e.g., via AddSingleton<IConnectionMultiplexer>(...) in Phase 2D.1 Redis setup).
            services.AddScoped<IDistributedLockService, RedisDistributedLockService>();
        }
        else
        {
            services.AddScoped<IDistributedLockService, SqlDistributedLockService>();
        }

        return services;
    }
}
