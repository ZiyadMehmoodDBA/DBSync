using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    // Partial method — filled in by Task 2 when RedisCacheService exists.
    // Declared as a separate private method so Task 2 can replace the body
    // without touching the public method signature.
    private static void RegisterRedis(IServiceCollection services)
    {
        // Task 2 replaces this body with Redis registration.
        // For Task 1, this path is unreachable when Provider=Memory (the default).
        throw new InvalidOperationException(
            "Redis provider is not yet wired. Add RedisCacheService (Task 2) first.");
    }
}
