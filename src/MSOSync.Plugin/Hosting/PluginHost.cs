using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Hosting;

public sealed class PluginHost(
    IPluginRuntimeManager       runtimeManager,
    IPluginRegistry             registry,
    IPluginLoader               loader,
    ILogger<PluginHost>         logger) : IHostedService, IPluginHost
{
    public bool      IsStarted         { get; private set; }
    public DateTime? StartedAt         { get; private set; }
    public long      StartupDurationMs { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        try
        {
            await runtimeManager.LoadAndActivateAsync(cancellationToken);
            await runtimeManager.InitializeAsync(cancellationToken);
            await runtimeManager.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during plugin host startup");
        }

        registry.MarkInitialized();
        total.Stop();

        StartedAt         = DateTime.UtcNow;
        StartupDurationMs = total.ElapsedMilliseconds;
        IsStarted         = true;

        var all         = registry.GetAll();
        var discovered  = all.Count;
        var running     = all.Count(p => p.Status == PluginStatus.Running);
        var initialized = all.Count(p => p.Status == PluginStatus.Initialized);
        var failed      = all.Count(p => p.Status == PluginStatus.Failed);
        var disabled    = all.Count(p => p.Status == PluginStatus.Disabled);

        logger.Log(LogLevel.Information, PluginLogEvents.PluginStartupSummary,
            "Plugin host started. Discovered={D} Running={R} Initialized={I} Failed={F} Disabled={Dis} " +
            "LoadElapsed={LE}ms InitializeElapsed={IE}ms StartElapsed={SE}ms TotalElapsed={TE}ms",
            discovered, running, initialized, failed, disabled,
            runtimeManager.LoadElapsedMs, runtimeManager.InitializeElapsedMs,
            runtimeManager.StartElapsedMs, total.ElapsedMilliseconds);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await runtimeManager.StopAsync(cancellationToken);
            await runtimeManager.DisposeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error during plugin host shutdown");
        }

        foreach (var ctx in loader.LoadContexts)
        {
            try { ctx.Unload(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error unloading plugin AssemblyLoadContext");
            }
        }
    }
}
