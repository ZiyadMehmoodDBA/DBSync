using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common;

namespace MSOSync.Engine;

public static class SyncEngineExtensions
{
    public static IServiceCollection AddSyncEngine(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SyncEngine>());

        // IMetricsService — singleton ring-buffer implementation (Phase 2F swaps in OpenTelemetry)
        services.AddSingleton<IMetricsService, InMemoryMetricsService>();

        // ITransportService registered by AddTransportServices() in MSOSync.Transport
        services.AddScoped<SyncEngine>();
        return services;
    }
}
