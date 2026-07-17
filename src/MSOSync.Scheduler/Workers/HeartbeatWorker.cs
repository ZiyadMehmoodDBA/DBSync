using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Transport;

namespace MSOSync.Scheduler.Workers;

public sealed class HeartbeatWorker : BackgroundService
{
    private static readonly Meter          Meter = new("MSOSync.Heartbeat", "1.0.0");
    private static readonly Counter<long>  Sent  = Meter.CreateCounter<long>(
        "msosync_heartbeat_sent_total", description: "Total heartbeat POST requests sent");

    private readonly IServiceScopeFactory      _scopeFactory;
    private readonly IOptions<NodeProperties>  _nodeProps;
    private readonly IOptions<HeartbeatOptions> _heartbeatOptions;
    private readonly ILogger<HeartbeatWorker>  _logger;
    private readonly IWorkerStatusRegistry     _registry;
    private readonly DateTime                  _startTime = DateTime.UtcNow;

    public HeartbeatWorker(
        IServiceScopeFactory      scopeFactory,
        IOptions<NodeProperties>  nodeProps,
        IOptions<HeartbeatOptions> heartbeatOptions,
        ILogger<HeartbeatWorker>  logger,
        IWorkerStatusRegistry     registry)
    {
        _scopeFactory      = scopeFactory;
        _nodeProps         = nodeProps;
        _heartbeatOptions  = heartbeatOptions;
        _logger            = logger;
        _registry          = registry;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var intervalSeconds = _heartbeatOptions.Value.IntervalSeconds;
        if (intervalSeconds <= 0) intervalSeconds = 30;
        _registry.Register(nameof(HeartbeatWorker), TimeSpan.FromSeconds(intervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props    = _nodeProps.Value;
        var interval = TimeSpan.FromSeconds(_heartbeatOptions.Value.IntervalSeconds);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            _registry.RecordTickStart(nameof(HeartbeatWorker));
            try
            {
                await using var scope      = _scopeFactory.CreateAsyncScope();
                var httpClient = scope.ServiceProvider.GetRequiredService<INodeHttpClient>();

                var request = new MSOSync.Metadata.Dtos.HeartbeatRequest(
                    NodeId:        props.NodeId,
                    NodeVersion:   typeof(HeartbeatWorker).Assembly.GetName().Version?.ToString(),
                    UptimeSeconds: (long)(DateTime.UtcNow - _startTime).TotalSeconds,
                    DatabaseType:  "SqlServer",
                    TransportMode: null);

                await httpClient.PostAsync<MSOSync.Metadata.Dtos.HeartbeatRequest, object>(
                    $"{props.SyncUrl}/api/v1/nodes/{props.NodeId}/heartbeat",
                    request,
                    props.NodeId,
                    props.NodeToken,
                    ct);

                Sent.Add(1);
                _registry.RecordTickComplete(nameof(HeartbeatWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _registry.RecordTickFailed(nameof(HeartbeatWorker), ex);
                _logger.LogWarning(ex, "HeartbeatWorker: heartbeat send failed");
            }
        }
    }
}
