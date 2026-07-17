using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;
using MSOSync.Transport;

namespace MSOSync.Scheduler.Workers;

/// Telemetry-only probe worker — writes LastProbeTime/Latency/Error/ConsecutiveProbeFailures via
/// ExecuteUpdateAsync (bypasses RowVersion token). Does NOT write ConnectivityStatus or publish
/// NodeConnectivityChangedEvent — that is owned by ConnectivityEvaluator (Invariant 3, spec §5.1).
public sealed class ProbeWorker(
    IServiceScopeFactory       scopeFactory,
    IOptions<NodeProperties>   nodeProps,
    IOptions<LifecycleOptions> lifecycleOptions,
    IOptions<HeartbeatOptions> heartbeatOptions,
    ILogger<ProbeWorker>       logger,
    IWorkerStatusRegistry      registry) : BackgroundService
{
    private static readonly Meter         Meter   = new("MSOSync.Probe", "1.0.0");
    private static readonly Counter<long> Success = Meter.CreateCounter<long>("msosync_probe_success_total");
    private static readonly Counter<long> Failure = Meter.CreateCounter<long>("msosync_probe_failure_total");

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var intervalSeconds = heartbeatOptions.Value.ProbeIntervalSeconds;
        registry.Register(nameof(ProbeWorker), TimeSpan.FromSeconds(intervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props    = nodeProps.Value;
        var interval = TimeSpan.FromSeconds(heartbeatOptions.Value.ProbeIntervalSeconds);

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var topology = scope.ServiceProvider.GetRequiredService<ITopologyService>();
            if (!await topology.IsHubAsync(props.NodeId, ct))
            {
                logger.LogInformation("ProbeWorker disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            registry.RecordTickStart(nameof(ProbeWorker));
            try
            {
                await RunProbeTickAsync(props.NodeId, ct);
                registry.RecordTickComplete(nameof(ProbeWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(ProbeWorker), ex);
                logger.LogError(ex, "ProbeWorker tick failed");
            }
        }
    }

    private async Task RunProbeTickAsync(string localNodeId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var httpClient = scope.ServiceProvider.GetRequiredService<INodeHttpClient>();

        var probeStates = new[] { NodeLifecycleState.Active, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning };
        var query = db.Nodes.AsNoTracking()
            .Where(n => n.UpstreamNodeId == localNodeId && probeStates.Contains(n.LifecycleState));
        if (!lifecycleOptions.Value.MaintenanceContinueProbing)
            query = query.Where(n => !n.MaintenanceMode);

        var children = await query.ToListAsync(ct);

        foreach (var child in children)
        {
            var sw  = Stopwatch.StartNew();
            var now = DateTime.UtcNow;

            try
            {
                await httpClient.PostAsync<object, object>(
                    $"{child.SyncUrl}/api/v1/sync/ping", new { }, child.NodeId, string.Empty, ct);
                sw.Stop();
                var latencyMs = (int)sw.ElapsedMilliseconds;

                await db.Nodes.Where(n => n.NodeId == child.NodeId).ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.LastProbeTime, now)
                    .SetProperty(n => n.LastProbeLatencyMs, latencyMs)
                    .SetProperty(n => n.LastProbeError, (string?)null)
                    .SetProperty(n => n.ConsecutiveProbeFailures, 0), ct);

                Success.Add(1);
                logger.LogDebug("ProbeWorker: {NodeId} reachable ({Ms}ms)", child.NodeId, latencyMs);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var errorMessage = ex.Message;
                var trimmed = errorMessage.Length > 512 ? errorMessage[..512] : errorMessage;

                await db.Nodes.Where(n => n.NodeId == child.NodeId).ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.LastProbeTime, now)
                    .SetProperty(n => n.LastProbeLatencyMs, (int?)null)
                    .SetProperty(n => n.LastProbeError, trimmed)
                    .SetProperty(n => n.ConsecutiveProbeFailures, n => n.ConsecutiveProbeFailures + 1), ct);

                Failure.Add(1);
                logger.LogDebug("ProbeWorker: {NodeId} probe failed — {Error}", child.NodeId, trimmed);
            }
        }
    }
}
