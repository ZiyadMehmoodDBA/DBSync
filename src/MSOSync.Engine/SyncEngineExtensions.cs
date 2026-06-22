using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Engine;

public static class SyncEngineExtensions
{
    public static IServiceCollection AddSyncEngine(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SyncEngine>());
        services.AddScoped<ITransportService, NoOpTransportService>();
        services.AddScoped<SyncEngine>();
        return services;
    }
}
