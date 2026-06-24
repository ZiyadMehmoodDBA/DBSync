using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Metadata.Nodes;
using MSOSync.Persistence;
using MSOSync.Topology;

namespace MSOSync.Scheduler.Workers;

public sealed class NodeStatusWorker(
    IServiceScopeFactory      scopeFactory,
    IOptions<NodeProperties>  nodeProps,
    IConfiguration            config,
    ILogger<NodeStatusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var topology = scope.ServiceProvider.GetRequiredService<ITopologyService>();
            if (!await topology.IsHubAsync(props.NodeId, ct))
            {
                logger.LogInformation("NodeStatusWorker disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        var checkInterval = TimeSpan.FromSeconds(
            config.GetValue<int>("Heartbeat:StatusCheckIntervalSeconds", 60));

        using var timer = new PeriodicTimer(checkInterval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await RunTickAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "NodeStatusWorker tick failed"); }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stateMachine = scope.ServiceProvider.GetRequiredService<INodeStateMachine>();

        var heartbeatIntervalSec = config.GetValue<int>("Heartbeat:IntervalSeconds",  30);
        var missedThreshold      = config.GetValue<int>("Heartbeat:MissedThreshold",   3);
        var cutoff               = DateTime.UtcNow.AddSeconds(-(heartbeatIntervalSec * missedThreshold));

        var stale = await db.Nodes.AsNoTracking()
            .Where(n => n.Status == "REGISTERED"
                     && n.LastHeartbeat != null
                     && n.LastHeartbeat < cutoff)
            .Select(n => n.NodeId)
            .ToListAsync(ct);

        foreach (var nodeId in stale)
        {
            try
            {
                await stateMachine.TransitionAsync(nodeId, "OFFLINE", ct);
                logger.LogInformation("NodeStatusWorker: {NodeId} → OFFLINE (missed heartbeat)", nodeId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NodeStatusWorker: could not transition {NodeId} to OFFLINE", nodeId);
            }
        }
    }
}
