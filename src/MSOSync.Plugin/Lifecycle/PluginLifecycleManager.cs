using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Plugin.Loading;
using MSOSync.Plugin.Models;
using MSOSync.Plugin.Registry;

namespace MSOSync.Plugin.Lifecycle;

public sealed class PluginLifecycleManager(
    PluginRegistry               registry,
    IOptions<PluginHostOptions>  options,
    ILogger<PluginLifecycleManager> logger)
{
    public async Task InitializeAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForStartup();
        foreach (var rt in runtimes)
        {
            if (rt.State != PluginRuntimeState.Loaded) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Initializing;
            var sw   = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
                cts.CancelAfter(TimeSpan.FromSeconds(InitTimeout));

                using (BeginPhaseScope(rt.Descriptor.PluginId, rt.Descriptor.Version, "Initialize"))
                {
                    await rt.Instance.InitializeAsync(rt.Context!, cts.Token);
                }

                sw.Stop();
                rt.InitializeDuration = sw.Elapsed;
                rt.InitializedAt      = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.InitializedAt.Value;
                rt.State              = PluginRuntimeState.Initialized;

                rt.Descriptor.InitializeDurationMs = (long)rt.InitializeDuration!.Value.TotalMilliseconds;
                rt.Descriptor.InitializedAt        = rt.InitializedAt;
                rt.Descriptor.Status               = PluginStatus.Initialized;

                logger.Log(LogLevel.Information, PluginLogEvents.PluginInitialized,
                    "Plugin {PluginId} initialized in {Ms}ms", rt.Descriptor.PluginId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
            {
                sw.Stop();
                var msg = $"InitializeAsync timed out after {InitTimeout}s";
                SetFailed(rt, msg, sw.Elapsed);
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginTimeout,
                    "Plugin {PluginId} InitializeAsync timed out", rt.Descriptor.PluginId);
            }
            catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
            {
                throw;  // normal host shutdown — do not touch plugin state
            }
            catch (Exception ex)
            {
                sw.Stop();
                SetFailed(rt, ex.Message, sw.Elapsed);
                logger.Log(LogLevel.Error, PluginLogEvents.PluginFailed,
                    ex, "Plugin {PluginId} InitializeAsync failed", rt.Descriptor.PluginId);
            }
        }
    }

    public async Task StartAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForStartup();
        foreach (var rt in runtimes)
        {
            if (rt.State != PluginRuntimeState.Initialized) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Starting;
            var sw   = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
                cts.CancelAfter(TimeSpan.FromSeconds(StartTimeout));

                using (BeginPhaseScope(rt.Descriptor.PluginId, rt.Descriptor.Version, "Start"))
                {
                    await rt.Instance.StartAsync(cts.Token);
                }

                sw.Stop();
                rt.StartDuration      = sw.Elapsed;
                rt.StartedAt          = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.StartedAt.Value;
                rt.State              = PluginRuntimeState.Running;

                // TotalDuration = LoadDurationMs (from descriptor) + initialize + start
                var loadMs = TimeSpan.FromMilliseconds(rt.Descriptor.LoadDurationMs);
                rt.TotalDuration = loadMs + (rt.InitializeDuration ?? TimeSpan.Zero) + sw.Elapsed;

                rt.Descriptor.StartDurationMs  = (long)rt.StartDuration!.Value.TotalMilliseconds;
                rt.Descriptor.TotalDurationMs  = (long)(rt.TotalDuration?.TotalMilliseconds ?? 0);
                rt.Descriptor.StartedAt        = rt.StartedAt;
                rt.Descriptor.Status           = PluginStatus.Running;

                logger.Log(LogLevel.Information, PluginLogEvents.PluginStarted,
                    "Plugin {PluginId} started in {Ms}ms", rt.Descriptor.PluginId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
            {
                sw.Stop();
                SetFailed(rt, $"StartAsync timed out after {StartTimeout}s", sw.Elapsed);
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginTimeout,
                    "Plugin {PluginId} StartAsync timed out", rt.Descriptor.PluginId);
            }
            catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
            {
                throw;  // normal host shutdown — do not touch plugin state
            }
            catch (Exception ex)
            {
                sw.Stop();
                SetFailed(rt, ex.Message, sw.Elapsed);
                logger.Log(LogLevel.Error, PluginLogEvents.PluginFailed,
                    ex, "Plugin {PluginId} StartAsync failed", rt.Descriptor.PluginId);
            }
        }
    }

    public async Task StopAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForShutdown();
        foreach (var rt in runtimes)
        {
            if (rt.State is not (PluginRuntimeState.Running or PluginRuntimeState.Initialized)) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Stopping;
            var sw   = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
                cts.CancelAfter(TimeSpan.FromSeconds(StopTimeout));

                using (BeginPhaseScope(rt.Descriptor.PluginId, rt.Descriptor.Version, "Stop"))
                {
                    await rt.Instance.StopAsync(cts.Token);
                }

                sw.Stop();
                rt.StopDuration       = sw.Elapsed;
                rt.StoppedAt          = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.StoppedAt.Value;
                rt.State              = PluginRuntimeState.Stopped;
                rt.Descriptor.Status  = PluginStatus.Stopped;

                logger.Log(LogLevel.Information, PluginLogEvents.PluginStopped,
                    "Plugin {PluginId} stopped in {Ms}ms", rt.Descriptor.PluginId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!hostCt.IsCancellationRequested)
            {
                sw.Stop();
                SetFailed(rt, $"StopAsync timed out after {StopTimeout}s", sw.Elapsed);
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginTimeout,
                    "Plugin {PluginId} StopAsync timed out", rt.Descriptor.PluginId);
            }
            catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
            {
                throw;  // normal host shutdown — do not touch plugin state
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Stop exceptions are non-fatal — log and continue
                rt.StopDuration       = sw.Elapsed;
                rt.StoppedAt          = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.StoppedAt.Value;
                rt.State              = PluginRuntimeState.Stopped;
                rt.Descriptor.Status  = PluginStatus.Stopped;
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginFailed,
                    ex, "Plugin {PluginId} StopAsync threw; treating as stopped", rt.Descriptor.PluginId);
            }
        }
    }

    public async Task DisposeAllAsync(CancellationToken hostCt)
    {
        var runtimes = SortedForShutdown();
        foreach (var rt in runtimes)
        {
            if (rt.State is PluginRuntimeState.Disposed or PluginRuntimeState.Disabled) continue;
            if (rt.Instance is null) continue;

            rt.State = PluginRuntimeState.Disposing;
            var sw   = Stopwatch.StartNew();
            try
            {
                await rt.Instance.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.Log(LogLevel.Warning, PluginLogEvents.PluginFailed,
                    ex, "Plugin {PluginId} DisposeAsync threw; ignoring", rt.Descriptor.PluginId);
            }
            finally
            {
                sw.Stop();
                rt.DisposeDuration    = sw.Elapsed;
                rt.DisposedAt         = DateTime.UtcNow;
                rt.LastStateChangeUtc = rt.DisposedAt.Value;
                rt.State              = PluginRuntimeState.Disposed;

                logger.Log(LogLevel.Debug, PluginLogEvents.PluginDisposed,
                    "Plugin {PluginId} disposed", rt.Descriptor.PluginId);
            }
        }
    }

    private List<PluginRuntime> SortedForStartup()
        => registry.GetAllRuntimes()
            .OrderBy(r => r.Descriptor.StartupOrder)
            .ThenBy(r => r.Descriptor.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private List<PluginRuntime> SortedForShutdown()
        => registry.GetAllRuntimes()
            .OrderByDescending(r => r.Descriptor.StartupOrder)
            .ThenByDescending(r => r.Descriptor.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void SetFailed(PluginRuntime rt, string message, TimeSpan elapsed)
    {
        // Assign duration based on which phase was in progress
        switch (rt.State)
        {
            case PluginRuntimeState.Initializing: rt.InitializeDuration = elapsed; break;
            case PluginRuntimeState.Starting:     rt.StartDuration      = elapsed; break;
            case PluginRuntimeState.Stopping:     rt.StopDuration       = elapsed; break;
        }
        rt.LastException           = new InvalidOperationException(message);
        rt.State                   = PluginRuntimeState.Failed;
        rt.LastStateChangeUtc      = DateTime.UtcNow;
        rt.Descriptor.Status       = PluginStatus.Failed;
        rt.Descriptor.ErrorMessage = message;
    }

    private IDisposable? BeginPhaseScope(string pluginId, string version, string phase)
        => logger.BeginScope(new Dictionary<string, object>
        {
            ["PluginId"]      = pluginId,
            ["PluginVersion"] = version,
            ["Phase"]         = phase,
        });

    private int InitTimeout    => options.Value.InitializeTimeoutSeconds ?? options.Value.DefaultTimeoutSeconds;
    private int StartTimeout   => options.Value.StartTimeoutSeconds      ?? options.Value.DefaultTimeoutSeconds;
    private int StopTimeout    => options.Value.StopTimeoutSeconds       ?? options.Value.DefaultTimeoutSeconds;
    private int DisposeTimeout => options.Value.DisposeTimeoutSeconds    ?? options.Value.DefaultTimeoutSeconds;
}
