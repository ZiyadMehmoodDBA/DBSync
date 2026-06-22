// src/MSOSync.Trigger/TriggerServiceExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Trigger;

public static class TriggerServiceExtensions
{
    public static IServiceCollection AddTriggerEngine(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddSingleton<SqlServerTriggerBuilder>();
        services.AddScoped<ITriggerInstallationService, TriggerInstallationService>();
        services.AddScoped<ITriggerDriftDetector, TriggerDriftDetector>();
        return services;
    }
}
