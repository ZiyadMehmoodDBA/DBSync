using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Transport;

namespace MSOSync.Scheduler.Workers;

public sealed class HeartbeatWorker : BackgroundService
{
    private static readonly Meter          Meter = new("MSOSync.Heartbeat", "1.0.0");
    private static readonly Counter<long>  Sent  = Meter.CreateCounter<long>(
        "msosync_heartbeat_sent_total", description: "Total heartbeat POST requests sent");

    private readonly IServiceScopeFactory     _scopeFactory;
    private readonly IOptions<NodeProperties> _nodeProps;
    private readonly IConfiguration           _config;
    private readonly ILogger<HeartbeatWorker> _logger;
    private readonly DateTime                 _startTime = DateTime.UtcNow;

    public HeartbeatWorker(
        IServiceScopeFactory     scopeFactory,
        IOptions<NodeProperties> nodeProps,
        IConfiguration           config,
        ILogger<HeartbeatWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _nodeProps    = nodeProps;
        _config       = config;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props    = _nodeProps.Value;
        var interval = TimeSpan.FromSeconds(
            _config.GetValue<int>("Heartbeat:IntervalSeconds", 30));

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
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
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "HeartbeatWorker: heartbeat send failed");
            }
        }
    }
}
