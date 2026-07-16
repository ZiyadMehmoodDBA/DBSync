using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Configuration;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;
using MSOSync.Plugin.Runtime;
using MSOSync.Sdk.Abstractions;
using MSOSync.Sdk.Metadata;

namespace MSOSync.Plugin.Lifecycle;

internal sealed class PluginActivator(
    PluginRegistry              registry,
    ILoggerFactory              loggerFactory,
    IHostEnvironment            hostEnvironment,
    IConfiguration              configuration,
    IOptions<PluginHostOptions> options,
    ILogger<PluginActivator>    logger)
{
    public Task<bool> ActivateAsync(string pluginId, CancellationToken ct)
    {
        var runtime = registry.GetRuntime(pluginId);
        if (runtime is null)
        {
            logger.LogWarning("PluginActivator: no runtime found for {PluginId}", pluginId);
            return Task.FromResult(false);
        }

        var manifest = runtime.Descriptor.Manifest;
        var assembly = runtime.Assembly;

        if (manifest is null || assembly is null)
        {
            SetFailed(runtime, new InvalidOperationException("Assembly or manifest is null"));
            return Task.FromResult(false);
        }

        // Step 1: Resolve type
        var type = assembly.GetType(manifest.EntryType);
        if (type is null)
        {
            SetFailed(runtime,
                new InvalidOperationException($"Type '{manifest.EntryType}' not found in assembly"));
            return Task.FromResult(false);
        }

        // Step 2: Verify type implements IPlugin
        if (!typeof(IPlugin).IsAssignableFrom(type))
        {
            SetFailed(runtime,
                new InvalidOperationException($"Type '{manifest.EntryType}' does not implement IPlugin"));
            return Task.FromResult(false);
        }

        // Step 3: Verify public parameterless constructor
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            SetFailed(runtime,
                new InvalidOperationException($"Type '{manifest.EntryType}' must have a public parameterless constructor"));
            return Task.FromResult(false);
        }

        // Step 4: Build per-plugin sub-container
        var opts          = options.Value;
        var pluginDir     = Path.GetDirectoryName(assembly.Location) ?? opts.PluginsPath;
        var pluginLogger  = new PluginLoggerAdapter(loggerFactory.CreateLogger(pluginId));
        var configSection = configuration.GetSection($"Plugins:{pluginId}");
        var configFilePath = Path.Combine(pluginDir, "plugin.config.json");
        var configFile    = PluginConfigurationFile.Load(configFilePath, logger, opts.MaxPluginConfigSizeBytes);
        var pluginConfig  = new PluginConfigurationAdapter(configSection, configFile);
        var pluginEnv     = new PluginEnvironmentAdapter(hostEnvironment, opts, pluginDir);
        var metadata      = BuildMetadata(manifest);

        var services = new ServiceCollection();
        services.AddSingleton<IPluginLogger>(pluginLogger);
        services.AddSingleton<IPluginConfiguration>(pluginConfig);
        services.AddSingleton<IPluginEnvironment>(pluginEnv);
        services.AddSingleton<IPluginServices>(sp => new PluginServicesAdapter(sp));
        var pluginProvider = services.BuildServiceProvider();

        // Step 5: Create context (immutable — never replaced)
        var pluginServices = pluginProvider.GetRequiredService<IPluginServices>();
        var context        = new PluginContext(metadata, pluginLogger, pluginConfig, pluginServices, pluginEnv);

        // Step 6: Instantiate plugin
        IPlugin instance;
        try
        {
            instance = (IPlugin)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            SetFailed(runtime, ex);
            return Task.FromResult(false);
        }

        // Step 7: Store in runtime
        runtime.Instance       = instance;
        runtime.PluginServices = pluginProvider;
        runtime.Context        = context;

        logger.LogInformation("Plugin {PluginId} activated successfully", pluginId);
        return Task.FromResult(true);
    }

    private static void SetFailed(PluginRuntime runtime, Exception ex)
    {
        runtime.Descriptor.Status       = PluginStatus.Failed;
        runtime.Descriptor.ErrorMessage = ex.Message;
        runtime.State                   = PluginRuntimeState.Failed;
        runtime.LastException           = ex;
    }

    private static PluginMetadata BuildMetadata(PluginManifest manifest)
    {
        var caps = manifest.Capabilities
            .Select(c => Enum.TryParse<PluginCapability>(c, ignoreCase: true, out var v) ? v : (PluginCapability?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToHashSet();

        var perms = manifest.Permissions
            .Select(p => Enum.TryParse<PluginPermission>(p, ignoreCase: true, out var v) ? v : (PluginPermission?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToHashSet();

        return new PluginMetadata
        {
            PluginId     = manifest.Id,
            Name         = manifest.Name,
            Version      = manifest.Version,
            SdkVersion   = manifest.SdkVersion ?? "1.0",
            ApiVersion   = manifest.ApiVersion ?? "1",
            Author       = manifest.Author,
            Description  = manifest.Description,
            Capabilities = caps,
            Permissions  = perms,
        };
    }
}
