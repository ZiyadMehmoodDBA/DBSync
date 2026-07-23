using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Packaging.Abstractions;
using MSOSync.Plugin.Packaging.Installer;
using MSOSync.Plugin.Packaging.Models;
using MSOSync.Plugin.Packaging.Packager;
using MSOSync.Plugin.Security;
using MSOSync.Plugin.Signing;
using MSOSync.Plugin.Signing.Abstractions;

namespace MSOSync.Plugin.Hosting;

public static class PluginServiceExtensions
{
    public static IServiceCollection AddPluginCoreInternals(this IServiceCollection services)
    {
        services.AddSingleton<ISdkCompatibilityValidator, SdkCompatibilityValidator>();
        services.AddSingleton<PluginActivator>();
        services.AddSingleton<PluginLifecycleManager>();
        services.AddSingleton<PluginRuntimeManager>();
        services.AddSingleton<IPluginRuntimeManager>(sp =>
            sp.GetRequiredService<PluginRuntimeManager>());
        return services;
    }

    /// <summary>
    /// Registers plugin packaging and signing services.
    /// Call after <see cref="AddPluginCoreInternals"/> in Program.cs / Startup.cs.
    /// </summary>
    public static IServiceCollection AddPluginPackaging(
        this IServiceCollection services,
        IConfiguration           configuration)
    {
        services.Configure<PluginSecurityOptions>(
            configuration.GetSection("PluginSecurity"));
        services.Configure<PackagingOptions>(
            configuration.GetSection("PluginPackaging"));

        // Singleton: reads trusted-publishers.json once at startup
        services.AddSingleton<ITrustedPublisherRegistry, TrustedPublisherRegistry>();

        // Singleton: stateless verifier; holds reference to registry
        services.AddSingleton<IPluginSignatureVerifier, RsaPssSignatureVerifier>();

        // Scoped: per-request IO operations
        services.AddScoped<IPluginPackager, PluginPackager>();
        services.AddScoped<IPluginInstaller, PluginInstaller>();

        return services;
    }
}
