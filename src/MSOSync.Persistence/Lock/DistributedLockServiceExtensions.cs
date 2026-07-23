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

        // Redis branch added in Task 3 once RedisDistributedLockService exists.
        services.AddScoped<IDistributedLockService, SqlDistributedLockService>();

        return services;
    }
}
