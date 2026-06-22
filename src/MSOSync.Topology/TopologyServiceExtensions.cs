using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Topology;

public static class TopologyServiceExtensions
{
    public static IServiceCollection AddTopologyServices(this IServiceCollection services)
    {
        services.AddScoped<ITopologyService, TopologyService>();
        return services;
    }
}
