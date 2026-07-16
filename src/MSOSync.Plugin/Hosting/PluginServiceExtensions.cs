using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Lifecycle;

namespace MSOSync.Plugin.Hosting;

public static class PluginServiceExtensions
{
    public static IServiceCollection AddPluginCoreInternals(this IServiceCollection services)
    {
        services.AddSingleton<PluginActivator>();
        services.AddSingleton<PluginLifecycleManager>();
        services.AddSingleton<PluginRuntimeManager>();
        services.AddSingleton<IPluginRuntimeManager>(sp =>
            sp.GetRequiredService<PluginRuntimeManager>());
        return services;
    }
}
