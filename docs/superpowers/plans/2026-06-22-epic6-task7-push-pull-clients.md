# Task 7: PushClient + PullClient

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 5 (PushClient, PullClient)
**Depends on:** Tasks 3 (payloads, GzipCompressionService), 4 (INodeHttpClient)

**Files:**
- Create: `src/MSOSync.Transport/PushClient.cs`
- Create: `src/MSOSync.Transport/PullClient.cs`

**Interfaces:**
- Consumes: `INodeHttpClient`, `GzipCompressionService`, `IOptions<NodeProperties>`, wire payload records
- Produces: `PushClient.PushAsync(...)`, `PullClient.PullAsync(...)`
- Consumed by: Tasks 6 (SmartTransportService), 10 (PullJob)

---

- [ ] **Step 1: Create PushClient**

Create `src/MSOSync.Transport/PushClient.cs`:
```csharp
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

public sealed class PushClient(
    INodeHttpClient        nodeHttp,
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
            e.TransactionId,
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
```

- [ ] **Step 2: Create PullClient**

Create `src/MSOSync.Transport/PullClient.cs`:
```csharp
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

        // Fire and return — source returns 200 for valid and duplicate ACKs
        await nodeHttp.PostAsync<AckPayload, PushResponse>(
            url, ack, props.NodeId, props.NodeToken, ct);
    }
}
```

- [ ] **Step 3: Build to verify**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: zero warnings, zero errors.

- [ ] **Step 4: Commit**

```pwsh
git add src/MSOSync.Transport/PushClient.cs
git add src/MSOSync.Transport/PullClient.cs
git commit -m "feat(epic6): PushClient and PullClient using INodeHttpClient"
```
