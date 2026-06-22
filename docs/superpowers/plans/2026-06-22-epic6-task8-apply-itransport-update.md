# Task 8: IApplyService + Update ITransportService + Update SyncEngine

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 1, § 5 (IApplyService)
**Depends on:** Tasks 1 (IncomingBatchStatus), 3 (payload types), 6 (SmartTransportService)

**Files:**
- Create: `src/MSOSync.Transport/IApplyService.cs`
- Create: `src/MSOSync.Transport/ApplyResult.cs`
- Create: `src/MSOSync.Transport/NoOpApplyService.cs`
- Modify: `src/MSOSync.Engine/ITransportService.cs` — add `events` parameter
- Modify: `src/MSOSync.Engine/SyncEngine.cs` — update call site
- Delete: `src/MSOSync.Engine/NoOpTransportService.cs`
- Modify: `src/MSOSync.Engine/SyncEngineExtensions.cs` — remove NoOp registration
- Modify: `src/MSOSync.Transport/SmartTransportService.cs` — align with new interface

**Interfaces:**
- Produces: `IApplyService`, `ApplyResult`, `NoOpApplyService`; updated `ITransportService` signature
- Consumed by: Tasks 9 (SyncController), 10 (PullJob)

---

- [ ] **Step 1: Create IApplyService + ApplyResult**

Create `src/MSOSync.Transport/ApplyResult.cs`:
```csharp
namespace MSOSync.Transport;

public sealed record ApplyResult(
    bool    Success,
    int     AppliedRows,
    int     ErrorRows,
    string? ErrorMessage);
```

Create `src/MSOSync.Transport/IApplyService.cs`:
```csharp
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

public interface IApplyService
{
    Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Create NoOpApplyService**

Create `src/MSOSync.Transport/NoOpApplyService.cs`:
```csharp
using Microsoft.Extensions.Logging;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport.Payloads;

namespace MSOSync.Transport;

/// <summary>
/// Apply stub. Transitions batch through New→Applying→Applied and returns success.
/// Epic 7 replaces this single registration with the real SQL apply engine.
/// </summary>
public sealed class NoOpApplyService(
    AppDbContext db,
    ILogger<NoOpApplyService> logger) : IApplyService
{
    public async Task<ApplyResult> ApplyAsync(
        SyncIncomingBatch incoming,
        BatchPayload      payload,
        CancellationToken ct = default)
    {
        // New → Applying
        incoming.Status = IncomingBatchStatus.Applying;
        await db.SaveChangesAsync(ct);

        logger.LogDebug("NoOpApplyService: applying batch {BatchId} ({RowCount} rows) — stub",
            incoming.BatchId, payload.RowCount);

        // Applying → Applied
        incoming.Status      = IncomingBatchStatus.Applied;
        incoming.AppliedTime = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new ApplyResult(true, payload.RowCount, 0, null);
    }
}
```

Note: `NoOpApplyService` directly mutates the tracked entity (already loaded from DB by caller). Epic 7 replaces this with real SQL execution against the destination tables.

- [ ] **Step 3: Update ITransportService — add events parameter**

Replace `src/MSOSync.Engine/ITransportService.cs`:
```csharp
using MSOSync.Persistence.Entities;

namespace MSOSync.Engine;

public interface ITransportService
{
    Task SendBatchAsync(
        SyncOutgoingBatch            batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken            ct = default);
}
```

- [ ] **Step 4: Update SyncEngine call site**

In `src/MSOSync.Engine/SyncEngine.cs`, the line at step 5 (send each batch) currently reads:
```csharp
        // 5. Send each batch (no-op this epic)
        foreach (var batch in batches)
            await transport.SendBatchAsync(batch, ct);
```

Replace with:
```csharp
        // 5. Send each batch via transport (PUSH or PULL no-op)
        foreach (var batch in batches)
            await transport.SendBatchAsync(batch, events, ct);
```

The `events` variable is in scope from step 2 (`var events = await eventReader.ReadAsync(...)`).

- [ ] **Step 5: Update SmartTransportService to match new interface**

In `src/MSOSync.Transport/SmartTransportService.cs`, if using the temporary single-param shim from Task 6, collapse it to the final two-param public signature:

```csharp
    public async Task SendBatchAsync(
        SyncOutgoingBatch            batch,
        IReadOnlyList<SyncDataEvent> events,
        CancellationToken            ct = default)
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
            logger.LogDebug("Transport: node {NodeId} is Pull — batch {BatchId} awaits pull",
                batch.NodeId, batch.BatchId);
            return;
        }

        await stateMachine.MoveToSendingAsync(batch.BatchId, ct);

        try
        {
            var result  = await pushClient.PushAsync(node.SyncUrl, batch, events, ct);
            var ackTime = DateTimeOffset.UtcNow;
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, result.Success, ackTime, result.ErrorMessage, ct);
        }
        catch (Exception ex)
        {
            var reason = classifier.Classify(ex);
            logger.LogError(ex, "Transport: push failed for batch {BatchId} — reason={Reason}",
                batch.BatchId, reason);
            await acknowledgement.AcknowledgeOutgoingAsync(
                batch.BatchId, false, DateTimeOffset.UtcNow, ex.Message, ct);
        }
    }
```

- [ ] **Step 6: Delete NoOpTransportService and update SyncEngineExtensions**

Delete `src/MSOSync.Engine/NoOpTransportService.cs`.

Replace `src/MSOSync.Engine/SyncEngineExtensions.cs`:
```csharp
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MSOSync.Engine;

public static class SyncEngineExtensions
{
    public static IServiceCollection AddSyncEngine(
        this IServiceCollection services,
        IConfiguration _)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SyncEngine>());
        // ITransportService registered by AddTransportServices() in MSOSync.Transport
        services.AddScoped<SyncEngine>();
        return services;
    }
}
```

- [ ] **Step 7: Build and test**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: build clean, all EngineTests pass. `SyncEngineTests` may need updating if they mock `ITransportService` — update those mocks to use the new two-param signature.

- [ ] **Step 8: Commit**

```pwsh
git add src/MSOSync.Transport/IApplyService.cs
git add src/MSOSync.Transport/ApplyResult.cs
git add src/MSOSync.Transport/NoOpApplyService.cs
git add src/MSOSync.Engine/ITransportService.cs
git add src/MSOSync.Engine/SyncEngine.cs
git add src/MSOSync.Engine/SyncEngineExtensions.cs
git add src/MSOSync.Transport/SmartTransportService.cs
git rm src/MSOSync.Engine/NoOpTransportService.cs
git commit -m "feat(epic6): IApplyService + NoOpApplyService, ITransportService gains events param, remove NoOp"
```
