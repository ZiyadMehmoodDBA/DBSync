using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Event;

public static class EventServiceExtensions
{
    public static IServiceCollection AddEventServices(this IServiceCollection services)
    {
        services.AddScoped<IEventReader, EventReader>();
        services.AddScoped<IEventPurger, EventPurger>();
        return services;
    }
}
