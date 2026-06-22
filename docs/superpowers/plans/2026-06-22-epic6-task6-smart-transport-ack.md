# Task 6: SmartTransportService + AcknowledgementService

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 4, § 5
**Depends on:** Tasks 1 (entities), 2 (IBatchStateMachine), 3 (failure types), 5 (IBatchTransportQueryService)

**Files:**
- Create: `src/MSOSync.Transport/SmartTransportService.cs`
- Create: `src/MSOSync.Transport/AcknowledgementService.cs`

**Interfaces:**
- Consumes: `INodeMetadataService` (from Metadata), `IBatchStateMachine` (from Batch), `IBatchTransportQueryService` (from Batch), `ITransportFailureClassifier`, `IClock`, `ILogger`
- Produces: `SmartTransportService : ITransportService`, `AcknowledgementService`
- Note: `ITransportService` signature change (add `events` parameter) happens in Task 8. For now, implement with the current single-param signature; Task 8 updates it.

---

- [ ] **Step 1: Create SmartTransportService**

`SmartTransportService` reads node metadata and dispatches PUSH or skips (PULL targets handled by PullJob):

Create `src/MSOSync.Transport/SmartTransportService.cs`:
```csharp
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Engine;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Transport;

public sealed class SmartTransportService(
    INodeMetadataService   nodeMetadata,
    PushClient             pushClient,
    IBatchStateMachine     stateMachine,
    AcknowledgementService acknowledgement,
    ITransportFailureClassifier classifier,
    ILogger<SmartTransportService> logger) : ITransportService
{
    public async Task SendBatchAsync(
        SyncOutgoingBatch          batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken          ct = default)
    {
        var node = await nodeMetadata.GetNodeAsync(batch.NodeId, ct);

        if (node == null)
        {
            logger.LogWarning("Transport: node {NodeId} not found — skipping batch {BatchId}",
                batch.NodeId, batch.BatchId);
            return;
        }

        if (!node.SyncEnabled)
        {
            logger.LogDebug("Transport: node {NodeId} sync disabled — skipping batch {BatchId}",
                batch.NodeId, batch.BatchId);
            return;
        }

        if (node.TransportMode == TransportMode.Pull)
        {
            // PULL target: source keeps batch as New; PullJob on the target will come fetch it
            logger.LogDebug("Transport: node {NodeId} is Pull — batch {BatchId} awaits pull",
                batch.NodeId, batch.BatchId);
            return;
        }

        // PUSH target: initiate send
        await stateMachine.MoveToSendingAsync(batch.BatchId, ct);

        try
        {
            var result = await pushClient.PushAsync(node.SyncUrl, batch, events, ct);
            var ackTime = DateTimeOffset.UtcNow;
            await acknowledgement.AcknowledgeOutgoingAsync(batch.BatchId, result.Success, ackTime,
                result.ErrorMessage, ct);
        }
        catch (Exception ex)
        {
            var reason = classifier.Classify(ex);
            logger.LogError(ex, "Transport: push failed for batch {BatchId} — reason={Reason}",
                batch.BatchId, reason);
            await acknowledgement.AcknowledgeOutgoingAsync(batch.BatchId, success: false,
                DateTimeOffset.UtcNow, ex.Message, ct);
        }
    }
}
```

Note: `ITransportService.SendBatchAsync` currently has signature `(SyncOutgoingBatch, CancellationToken)`. Task 8 adds the `events` parameter. For now, add the `events` param to this implementation — it will compile after Task 8 updates the interface. The implementer may need to temporarily match the old interface signature to compile, then Task 8 makes the interface match.

**Alternative (avoid compile ordering issue):** Implement with old interface signature and update in Task 8:
```csharp
public async Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct = default)
{
    // Pass empty list for now; Task 8 updates this to pass real events
    await SendBatchAsync(batch, [], ct);
}

private async Task SendBatchAsync(
    SyncOutgoingBatch batch, IReadOnlyList<SyncDataEvent> events, CancellationToken ct)
{
    // ... implementation above ...
}
```

This approach compiles against the current `ITransportService` interface and Task 8 moves the `events` param to the public interface.

- [ ] **Step 2: Create AcknowledgementService**

Create `src/MSOSync.Transport/AcknowledgementService.cs`:
```csharp
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

/// <summary>
/// Handles both outgoing ACK (PUSH mode — source side) and incoming ACK (PULL mode — from POST /ack).
/// </summary>
public sealed class AcknowledgementService(
    IBatchStateMachine stateMachine,
    AppDbContext       db,
    ILogger<AcknowledgementService> logger)
{
    /// <summary>
    /// Called by SmartTransportService after a PUSH attempt completes.
    /// </summary>
    public async Task AcknowledgeOutgoingAsync(
        long batchId, bool success, DateTimeOffset ackTime,
        string? errorMessage, CancellationToken ct = default)
    {
        if (success)
        {
            await stateMachine.MoveToAcknowledgedAsync(batchId, ackTime, ct);
            logger.LogInformation("Batch {BatchId} acknowledged at {AckTime}", batchId, ackTime);
        }
        else
        {
            await stateMachine.MoveToErrorAsync(batchId, ct);
            if (errorMessage != null)
            {
                db.BatchErrors.Add(new SyncBatchError
                {
                    BatchId      = batchId,
                    ConflictType = TransportFailureReason.HttpError.ToString(),
                    ErrorMessage = errorMessage
                });
                await db.SaveChangesAsync(ct);
            }
            logger.LogWarning("Batch {BatchId} push failed: {Error}", batchId, errorMessage);
        }
    }

    /// <summary>
    /// Called by SyncController POST /ack — handles ACK from a PULL target.
    /// Returns false if batch not found.
    /// Idempotent: already-Acknowledged batch returns true (no-op).
    /// </summary>
    public async Task<bool> AcknowledgeIncomingAsync(
        AckPayload payload, CancellationToken ct = default)
    {
        var batch = await db.OutgoingBatches.FindAsync([payload.BatchId], ct);
        if (batch == null) return false;

        // Idempotent: already acknowledged
        if (batch.Status == (byte)BatchStatus.Acknowledged)
        {
            logger.LogDebug("Batch {BatchId} already acknowledged — ignoring duplicate ACK", payload.BatchId);
            return true;
        }

        if (payload.Success)
        {
            await stateMachine.MoveToAcknowledgedAsync(payload.BatchId, payload.AckTime, ct);
        }
        else
        {
            await stateMachine.MoveToErrorAsync(payload.BatchId, ct);
            db.BatchErrors.Add(new SyncBatchError
            {
                BatchId      = payload.BatchId,
                ConflictType = payload.ErrorMessage?.StartsWith("SEQUENCE_GAP") == true
                    ? "SequenceGap"
                    : TransportFailureReason.ApplyFailure.ToString(),
                ErrorMessage = payload.ErrorMessage
            });
            await db.SaveChangesAsync(ct);
        }

        return true;
    }
}
```

- [ ] **Step 3: Build to verify**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: zero warnings. SmartTransportService implements the current `ITransportService` interface (single-param version). After Task 8 updates the interface, this file will be updated to match.

- [ ] **Step 4: Commit**

```pwsh
git add src/MSOSync.Transport/SmartTransportService.cs
git add src/MSOSync.Transport/AcknowledgementService.cs
git commit -m "feat(epic6): SmartTransportService dispatches Push/Pull; AcknowledgementService handles outgoing+incoming ACK"
```
