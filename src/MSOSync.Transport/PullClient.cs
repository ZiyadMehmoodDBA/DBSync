using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

public sealed class PullClient(
    INodeHttpClient          nodeHttp,
    IOptions<NodeProperties> nodeProps)
{
    /// <summary>
    /// Returns null on 204 No Content (no batches available).
    /// </summary>
    public async Task<PullResponse?> PullAsync(
        string            sourceSyncUrl,
        PullRequest       request,
        CancellationToken ct = default)
    {
        var props = nodeProps.Value;
        var url   = $"{sourceSyncUrl.TrimEnd('/')}/api/v1/sync/pull";

        return await nodeHttp.PostNullableAsync<PullRequest, PullResponse>(
            url, request, props.NodeId, props.NodeToken, ct);
    }

    /// <summary>
    /// Posts an ACK back to the source node after applying a pulled batch.
    /// Always returns 200 (duplicate ACKs are idempotent on source).
    /// </summary>
    public async Task PostAckAsync(
        string            sourceSyncUrl,
        AckPayload        ack,
        CancellationToken ct = default)
    {
        var props = nodeProps.Value;
        var url   = $"{sourceSyncUrl.TrimEnd('/')}/api/v1/sync/ack";

        await nodeHttp.PostAsync<AckPayload, PushResponse>(
            url, ack, props.NodeId, props.NodeToken, ct);
    }
}
