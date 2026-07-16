using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Diagnostics;
using MSOSync.Plugin.Hosting;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;

namespace MSOSync.Plugin.IntegrationTests;

internal static class PluginHostHarness
{
    private static string BaseDir
        => Path.GetDirectoryName(typeof(PluginHostHarness).Assembly.Location)!;

    /// <summary>
    /// Returns path inside TestAssets/plugins/[subdir].
    /// Pass string.Empty for the root plugins dir (contains all plugin subdirs).
    /// </summary>
    internal static string TestAssetPath(string subdir)
        => Path.Combine(BaseDir, "TestAssets", "plugins", subdir);

    /// <summary>
    /// Returns path to an isolated wrapper directory containing only the named plugin subdir.
    /// Used for tests that should load exactly one plugin.
    /// </summary>
    internal static string IsolatedPath(string wrapperName)
        => Path.Combine(BaseDir, "TestAssets", "isolated", wrapperName);

    internal static async Task<IHost> StartAsync(
        string pluginsDir,
        Dictionary<string, string?>? extraConfig = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["PluginHost:PluginsPath"] = pluginsDir,
            ["PluginHost:HostVersion"] = "1.0.0",
        };
        if (extraConfig is not null)
            foreach (var (k, v) in extraConfig) inMemory[k] = v;

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(inMemory);
        builder.Logging.ClearProviders();   // suppress output noise in test runner

        var services = builder.Services;
        services.Configure<PluginHostOptions>(builder.Configuration.GetSection("PluginHost"));
        services.AddSingleton<PluginRegistry>();
        services.AddSingleton<IPluginRegistry>(sp => sp.GetRequiredService<PluginRegistry>());
        services.AddSingleton<ISdkCompatibilityValidator, SdkCompatibilityValidator>();
        services.AddSingleton<PluginActivator>();
        services.AddSingleton<PluginLifecycleManager>();
        services.AddSingleton<IPluginLoader, PluginLoader>();
        services.AddSingleton<PluginRuntimeManager>();
        services.AddSingleton<IPluginRuntimeManager>(sp => sp.GetRequiredService<PluginRuntimeManager>());
        services.AddSingleton<PluginHost>();
        services.AddSingleton<IPluginHost>(sp => sp.GetRequiredService<PluginHost>());
        services.AddHostedService(sp => sp.GetRequiredService<PluginHost>());
        services.AddHealthChecks().AddCheck<PluginHealthCheck>("plugins");

        // PluginLoader needs IPluginStore — mock it to return empty list
        var storeMock = new Mock<IPluginStore>();
        storeMock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<PluginRecord>());
        storeMock.Setup(s => s.UpsertAsync(It.IsAny<PluginRecord>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);
        services.AddScoped<IPluginStore>(_ => storeMock.Object);

        configureServices?.Invoke(services);

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }
}
