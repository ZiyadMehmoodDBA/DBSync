using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Routing;

public static class RoutingServiceExtensions
{
    public static IServiceCollection AddRoutingServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<RoutingService>());
        services.AddSingleton<RouteCacheState>();
        services.AddScoped<IRoutingService, RoutingService>();
        return services;
    }
}
