using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Api.Health;

public static class HealthServiceExtensions
{
    public static IServiceCollection AddHealthScoringService(this IServiceCollection services)
    {
        services.AddScoped<IHealthScoringService, HealthScoringService>();
        return services;
    }
}
