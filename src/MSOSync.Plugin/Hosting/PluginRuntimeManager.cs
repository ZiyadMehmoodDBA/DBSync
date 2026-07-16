using System.Diagnostics;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Lifecycle;
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Hosting;

internal sealed class PluginRuntimeManager(
    IPluginLoader               loader,
    PluginActivator             activator,
    PluginLifecycleManager      lifecycle,
    IOptions<PluginHostOptions> options) : IPluginRuntimeManager
{
    public long LoadElapsedMs       { get; private set; }
    public long InitializeElapsedMs { get; private set; }
    public long StartElapsedMs      { get; private set; }
    public long TotalElapsedMs => LoadElapsedMs + InitializeElapsedMs + StartElapsedMs;

    public async Task LoadAndActivateAsync(CancellationToken ct)
    {
        var sw      = Stopwatch.StartNew();
        var results = await loader.LoadAllAsync(options.Value.PluginsPath, ct);
        LoadElapsedMs = sw.ElapsedMilliseconds;

        foreach (var r in results.Where(r => r.Outcome == PluginLoadOutcome.Success))
        {
            await activator.ActivateAsync(r.PluginId, ct);
        }
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await lifecycle.InitializeAllAsync(ct);
        InitializeElapsedMs = sw.ElapsedMilliseconds;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await lifecycle.StartAllAsync(ct);
        StartElapsedMs = sw.ElapsedMilliseconds;
    }

    public async Task StopAsync(CancellationToken ct)
        => await lifecycle.StopAllAsync(ct);

    public async Task DisposeAsync(CancellationToken ct)
        => await lifecycle.DisposeAllAsync(ct);
}
