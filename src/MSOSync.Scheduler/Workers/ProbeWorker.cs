using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Topology;
using MSOSync.Transport;

namespace MSOSync.Scheduler.Workers;

public sealed class ProbeWorker(
    IServiceScopeFactory     scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IConfiguration           config,
    ILogger<ProbeWorker>     logger) : BackgroundService
{
    private static readonly Meter         Meter   = new("MSOSync.Probe", "1.0.0");
    private static readonly Counter<long> Success = Meter.CreateCounter<long>("msosync_probe_success_total");
    private static readonly Counter<long> Failure = Meter.CreateCounter<long>("msosync_probe_failure_total");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props    = nodeProps.Value;
        var interval = TimeSpan.FromSeconds(
            config.GetValue<int>("Heartbeat:ProbeIntervalSeconds", 60));

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
            try { await RunProbeTickAsync(props.NodeId, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "ProbeWorker tick failed"); }
        }
    }

    private async Task RunProbeTickAsync(string localNodeId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var httpClient = scope.ServiceProvider.GetRequiredService<INodeHttpClient>();

        var children = await db.Nodes.AsNoTracking()
            .Where(n => n.UpstreamNodeId == localNodeId && n.SyncEnabled)
            .ToListAsync(ct);

        foreach (var child in children)
        {
            var sw     = Stopwatch.StartNew();
            ConnectivityStatus status;

            try
            {
                await httpClient.PostAsync<object, object>(
                    $"{child.SyncUrl}/api/v1/sync/ping", new { }, child.NodeId, string.Empty, ct);
                sw.Stop();

                status = sw.ElapsedMilliseconds switch
                {
                    < 500  => ConnectivityStatus.Reachable,
                    < 2000 => ConnectivityStatus.Degraded,
                    _      => ConnectivityStatus.Degraded
                };
                Success.Add(1);
            }
            catch
            {
                sw.Stop();
                status = ConnectivityStatus.Unreachable;
                Failure.Add(1);
            }

            await db.Nodes
                .Where(n => n.NodeId == child.NodeId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(n => n.ConnectivityStatus, status)
                     .SetProperty(n => n.LastProbeTime,      DateTime.UtcNow)
                     .SetProperty(n => n.LastProbeLatencyMs, (int)sw.ElapsedMilliseconds),
                    ct);

            logger.LogDebug("ProbeWorker: {NodeId} → {Status} ({Ms}ms)",
                child.NodeId, status, sw.ElapsedMilliseconds);
        }
    }
}
