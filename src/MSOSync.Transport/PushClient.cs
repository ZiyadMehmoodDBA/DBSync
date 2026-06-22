using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

public sealed class PushClient(
    INodeHttpClient          nodeHttp,
    IOptions<NodeProperties> nodeProps)
{
    public async Task<PushResponse> PushAsync(
        string                       targetSyncUrl,
        SyncOutgoingBatch            batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken            ct = default)
    {
        var props   = nodeProps.Value;
        var payload = BuildPayload(batch, events, props.NodeId);
        var url     = $"{targetSyncUrl.TrimEnd('/')}/api/v1/sync/push";

        return await nodeHttp.PostAsync<BatchPayload, PushResponse>(
            url, payload, props.NodeId, props.NodeToken, ct);
    }

    private static BatchPayload BuildPayload(
        SyncOutgoingBatch batch, IReadOnlyList<SyncDataEvent> events, string sourceNodeId) =>
        new(
            batch.BatchId,
            batch.BatchSequence,
            batch.ChannelId,
            sourceNodeId,
            batch.NodeId,         // NodeId on outgoing batch = target node
            batch.RowCount,
            events.Select(MapEvent).ToList().AsReadOnly());

    private static EventPayload MapEvent(SyncDataEvent e) =>
        new(
            e.EventId,
            e.TriggerId,
            MapEventType(e.EventType),
            e.TableName,
            e.TransactionId?.ToString(),   // long? → string?
            e.PkData,
            e.RowData);

    private static string MapEventType(char c) => c switch
    {
        'I' => "INSERT",
        'U' => "UPDATE",
        'D' => "DELETE",
        _   => c.ToString()
    };
}
