using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MSOSync.Common.Caching;

public static class CachingExtensions
{
    /// <summary>
    /// Registers ICacheService backed by either IMemoryCache or Redis,
    /// based on Cache:Provider in configuration ("Memory" or "Redis").
    /// </summary>
    public static IServiceCollection AddCacheService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.Section));

        var provider = configuration
            .GetSection(CacheOptions.Section)
            .GetValue<string>("Provider") ?? "Memory";

        if (string.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            RegisterRedis(services);
        }
        else
        {
            // Memory provider — ensure IMemoryCache is registered (idempotent)
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, InMemoryCacheService>();
        }

        return services;
    }

    private static void RegisterRedis(IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;

            if (string.IsNullOrWhiteSpace(opts.RedisConnectionString))
                throw new InvalidOperationException(
                    "Cache:RedisConnectionString must be set when Cache:Provider is \"Redis\".");

            return ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        });

        services.AddSingleton<ICacheService, RedisCacheService>();

        services.AddHealthChecks()
            .AddCheck<RedisCacheHealthCheck>("redis-cache");
    }
}
