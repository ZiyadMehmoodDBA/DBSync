using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Hosting;

public sealed class PluginHost(
    IPluginLoader               loader,
    IPluginRegistry             registry,
    IOptions<PluginHostOptions> pluginOptions,
    ILogger<PluginHost>         logger) : IHostedService, IPluginHost
{
    public bool      IsStarted         { get; private set; }
    public DateTime? StartedAt         { get; private set; }
    public long      StartupDurationMs { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var sw          = Stopwatch.StartNew();
        var pluginsPath = pluginOptions.Value.PluginsPath;

        IReadOnlyList<PluginLoadResult> results;
        try
        {
            results = await loader.LoadAllAsync(pluginsPath, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during plugin host startup");
            results = [];
        }

        registry.MarkInitialized();
        sw.Stop();

        StartedAt         = DateTime.UtcNow;
        StartupDurationMs = sw.ElapsedMilliseconds;
        IsStarted         = true;

        var total    = results.Count;
        var loaded   = results.Count(r => r.Outcome == PluginLoadOutcome.Success);
        var disabled = results.Count(r => r.Outcome == PluginLoadOutcome.Disabled);
        var failed   = results.Count(r => r.Outcome == PluginLoadOutcome.Failed);

        logger.Log(LogLevel.Information, PluginLogEvents.PluginStartupSummary,
            "Plugin host started in {Ms}ms. Discovered={Total} Loaded={Loaded} Disabled={Disabled} Failed={Failed}",
            sw.ElapsedMilliseconds, total, loaded, disabled, failed);

        foreach (var f in results.Where(r => r.Outcome == PluginLoadOutcome.Failed))
        {
            logger.Log(LogLevel.Warning, PluginLogEvents.PluginFailed,
                "Plugin {Id} failed at stage {Stage}: {Error}",
                f.PluginId, f.FailureStage, f.ErrorMessage);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var ctx in loader.LoadContexts)
        {
            try { ctx.Unload(); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error unloading plugin context");
            }
        }
        return Task.CompletedTask;
    }
}
