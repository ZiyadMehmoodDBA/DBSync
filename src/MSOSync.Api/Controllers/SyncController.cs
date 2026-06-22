using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/sync")]
public sealed class SyncController(
    IBatchTransportQueryService batchQuery,
    AcknowledgementService      acknowledgement,
    IApplyService               applyService,
    GzipCompressionService      compression,
    IOptions<NodeProperties>    nodeProps,
    INodeMetadataService        nodeMetadata,
    IClock                      clock,
    ILogger<SyncController>     logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(TransportJsonContext.Default.Options);

    // ── PULL: target requests batches from this source node ──────────────────

    [HttpPost("pull")]
    public async Task<IActionResult> Pull([FromBody] PullRequest req, CancellationToken ct)
    {
        var myNodeId = User.FindFirst("nodeId")?.Value;

        if (req.TargetNodeId != myNodeId)
        {
            logger.LogWarning("Pull: TargetNodeId {Target} != authenticated nodeId {Me}",
                req.TargetNodeId, myNodeId);
            return Unauthorized();
        }

        var (batch, moreAvailable) = await batchQuery.GetNextPullBatchAsync(
            req.TargetNodeId, req.ChannelId, req.AfterSequence, ct);

        if (batch == null) return NoContent();   // 204

        var events = await batchQuery.GetEventsForBatchAsync(batch.BatchId, ct);
        var props  = nodeProps.Value;

        var payload = new BatchPayload(
            batch.BatchId,
            batch.BatchSequence,
            batch.ChannelId,
            props.NodeId,        // source = us
            req.TargetNodeId,
            batch.RowCount,
            events.Select(MapEvent).ToList().AsReadOnly());

        var response = new PullResponse([payload], moreAvailable);
        var json     = JsonSerializer.Serialize(response, JsonOpts);
        var bytes    = Encoding.UTF8.GetBytes(json);
        var gzipped  = compression.Compress(bytes);

        Response.ContentType = "application/json";
        Response.Headers.ContentEncoding = "gzip";
        await Response.Body.WriteAsync(gzipped, ct);
        return new EmptyResult();
    }

    // ── PUSH: another node pushes a batch to us ───────────────────────────────

    [HttpPost("push")]
    public async Task<IActionResult> Push(CancellationToken ct)
    {
        BatchPayload payload;
        try
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            var decompressed = compression.Decompress(ms.ToArray());
            payload = JsonSerializer.Deserialize<BatchPayload>(decompressed, JsonOpts)
                      ?? throw new InvalidOperationException("Null payload");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Push: failed to decompress/deserialize request body");
            return BadRequest("Invalid compressed payload");
        }

        var myNodeId = User.FindFirst("nodeId")?.Value;
        if (payload.TargetNodeId != myNodeId)
        {
            logger.LogWarning("Push: TargetNodeId {Target} != authenticated nodeId {Me}",
                payload.TargetNodeId, myNodeId);
            return Unauthorized();
        }

        // Idempotency: duplicate push
        if (await batchQuery.IncomingBatchExistsAsync(payload.SourceNodeId, payload.BatchSequence, ct))
        {
            logger.LogDebug("Push: duplicate batch source={Source} seq={Seq} — returning 200",
                payload.SourceNodeId, payload.BatchSequence);
            return Ok(new PushResponse(payload.BatchId, true, 0, 0, null));
        }

        // Sequence check
        var lastSeq = await batchQuery.GetLastSequenceAsync(payload.SourceNodeId, payload.ChannelId, ct);
        if (lastSeq + 1 != payload.BatchSequence)
        {
            logger.LogWarning(
                "Push: sequence gap for source={Source} channel={Ch} expected={Expected} got={Got}",
                payload.SourceNodeId, payload.ChannelId, lastSeq + 1, payload.BatchSequence);
            return Conflict(new { code = "SEQUENCE_GAP" });
        }

        // Insert incoming batch
        var incoming = new SyncIncomingBatch
        {
            BatchId       = payload.BatchId,
            NodeId        = payload.TargetNodeId,
            ChannelId     = payload.ChannelId,
            SourceNodeId  = payload.SourceNodeId,
            BatchSequence = payload.BatchSequence,
            ReceivedTime  = clock.UtcNow,
            RowCount      = payload.RowCount,
            Status        = IncomingBatchStatus.New
        };

        await batchQuery.InsertIncomingBatchAsync(incoming, ct);

        // Apply
        var result = await applyService.ApplyAsync(incoming, payload, ct);

        return Ok(new PushResponse(
            payload.BatchId,
            result.Success,
            result.AppliedRows,
            result.ErrorRows,
            result.ErrorMessage));
    }

    // ── ACK: target acknowledges a batch pulled from us ──────────────────────

    [HttpPost("ack")]
    public async Task<IActionResult> Ack([FromBody] AckPayload payload, CancellationToken ct)
    {
        var found = await acknowledgement.AcknowledgeIncomingAsync(payload, ct);
        if (!found)
        {
            logger.LogWarning("Ack: batch {BatchId} not found", payload.BatchId);
            return NotFound();
        }
        return Ok();
    }

    // ── PING: health check ───────────────────────────────────────────────────

    [HttpPost("ping")]
    public async Task<IActionResult> Ping(CancellationToken ct)
    {
        var props   = nodeProps.Value;
        var ownNode = await nodeMetadata.GetNodeAsync(props.NodeId, ct);
        return Ok(new PingResponse(
            props.NodeId,
            ownNode?.Status ?? "Unknown",
            ownNode?.TransportMode ?? TransportMode.Pull));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static EventPayload MapEvent(SyncDataEvent e) =>
        new(e.EventId, e.TriggerId, MapEventType(e.EventType),
            e.TableName, e.TransactionId?.ToString(), e.PkData, e.RowData);

    private static string MapEventType(char c) => c switch
    {
        'I' => "INSERT", 'U' => "UPDATE", 'D' => "DELETE", _ => c.ToString()
    };
}
