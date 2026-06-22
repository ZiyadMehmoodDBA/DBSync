using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;

namespace MSOSync.Scheduler;

public sealed class PullJob(
    IServiceScopeFactory     scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IConfiguration           config,
    ILogger<PullJob>         logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;

        // Self-check: if this node is in PUSH mode, PullJob is disabled
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var nodeMeta = scope.ServiceProvider.GetRequiredService<INodeMetadataService>();
            var ownNode  = await nodeMeta.GetNodeAsync(props.NodeId, ct);
            if (ownNode?.TransportMode == TransportMode.Push)
            {
                logger.LogInformation("PullJob disabled — node {NodeId} is in Push mode", props.NodeId);
                return;
            }
        }

        var intervalSeconds = config.GetValue<int>("Sync:PullIntervalSeconds", 10);
        var interval        = TimeSpan.FromSeconds(intervalSeconds);
        using var timer     = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await RunTickAsync(props.NodeId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "PullJob tick failed");
            }
        }
    }

    private async Task RunTickAsync(string localNodeId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var channelMeta  = sp.GetRequiredService<IChannelMetadataService>();
        var topology     = sp.GetRequiredService<ITopologyService>();
        var batchQuery   = sp.GetRequiredService<IBatchTransportQueryService>();
        var pullClient   = sp.GetRequiredService<PullClient>();
        var applyService = sp.GetRequiredService<IApplyService>();
        var clock        = sp.GetRequiredService<IClock>();

        var channels = (await channelMeta.GetChannelsAsync(ct))
            .Where(c => c.Enabled)
            .OrderByDescending(c => c.Priority)
            .ToList();

        var sources = await topology.GetSourceNodesAsync(localNodeId, ct);

        foreach (var channel in channels)
        {
            foreach (var source in sources)
            {
                await PollSourceAsync(
                    source, channel.ChannelId, localNodeId,
                    batchQuery, pullClient, applyService, clock, ct);
            }
        }
    }

    private async Task PollSourceAsync(
        SourceNodeInfo              source,
        string                      channelId,
        string                      localNodeId,
        IBatchTransportQueryService batchQuery,
        PullClient                  pullClient,
        IApplyService               applyService,
        IClock                      clock,
        CancellationToken           ct)
    {
        var lastSeq = await batchQuery.GetLastSequenceAsync(source.NodeId, channelId, ct);

        while (true)
        {
            var request  = new PullRequest(localNodeId, channelId, lastSeq);
            var response = await pullClient.PullAsync(source.SyncUrl, request, ct);

            if (response == null)
            {
                // 204 No Content — nothing to pull
                logger.LogDebug("PullJob: no batches from {Source} channel {Ch}", source.NodeId, channelId);
                break;
            }

            foreach (var batch in response.Batches)
            {
                var applied = await ProcessBatchAsync(
                    batch, source, localNodeId, lastSeq, batchQuery, pullClient, applyService, clock, ct);
                if (applied)
                    lastSeq = batch.BatchSequence;
            }

            if (!response.MoreAvailable) break;
        }
    }

    private async Task<bool> ProcessBatchAsync(
        BatchPayload               batch,
        SourceNodeInfo             source,
        string                     localNodeId,
        long                       lastSeq,
        IBatchTransportQueryService batchQuery,
        PullClient                 pullClient,
        IApplyService              applyService,
        IClock                     clock,
        CancellationToken          ct)
    {
        // Sequence gap check
        if (lastSeq + 1 != batch.BatchSequence)
        {
            logger.LogWarning(
                "PullJob: sequence gap from {Source} channel {Ch}: expected {Exp} got {Got}",
                source.NodeId, batch.ChannelId, lastSeq + 1, batch.BatchSequence);

            await pullClient.PostAckAsync(source.SyncUrl,
                new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                    false, "SEQUENCE_GAP", DateTimeOffset.UtcNow), ct);
            return false;
        }

        // Duplicate check
        if (await batchQuery.IncomingBatchExistsAsync(source.NodeId, batch.BatchSequence, ct))
        {
            logger.LogDebug("PullJob: duplicate batch source={Source} seq={Seq} — sending idempotent ACK",
                source.NodeId, batch.BatchSequence);
            await pullClient.PostAckAsync(source.SyncUrl,
                new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                    true, null, DateTimeOffset.UtcNow), ct);
            return true;
        }

        // Insert and apply
        var incoming = new SyncIncomingBatch
        {
            BatchId       = batch.BatchId,
            NodeId        = localNodeId,
            ChannelId     = batch.ChannelId,
            SourceNodeId  = source.NodeId,
            BatchSequence = batch.BatchSequence,
            ReceivedTime  = clock.UtcNow,
            RowCount      = batch.RowCount,
            Status        = IncomingBatchStatus.New
        };

        await batchQuery.InsertIncomingBatchAsync(incoming, ct);
        var result  = await applyService.ApplyAsync(incoming, batch, ct);
        var ackTime = DateTimeOffset.UtcNow;

        await pullClient.PostAckAsync(source.SyncUrl,
            new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                result.Success, result.Success ? null : "APPLY_FAILURE", ackTime), ct);

        return result.Success;
    }
}
