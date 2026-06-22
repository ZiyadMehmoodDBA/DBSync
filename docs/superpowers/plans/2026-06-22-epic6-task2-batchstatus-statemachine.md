# Task 2: BatchStatus Rename + Named IBatchStateMachine + IClock Injection

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 2 (BatchStatus + IBatchStateMachine)
**Depends on:** Task 1 (entities)

**Files:**
- Modify: `src/MSOSync.Batch/BatchStatus.cs`
- Modify: `src/MSOSync.Batch/IBatchStateMachine.cs`
- Modify: `src/MSOSync.Batch/BatchStateMachine.cs`
- Modify: `src/MSOSync.Scheduler/SchedulerRecovery.cs`
- Modify: `tests/MSOSync.EngineTests/BatchStateMachineTests.cs`

**Interfaces:**
- Consumes: `IClock` (from `MSOSync.Common`), `AppDbContext`
- Produces: Updated `BatchStatus` enum (`Sending`, `Acknowledged`); `IBatchStateMachine` with 4 named methods; `BatchStateMachine` implementation

---

- [ ] **Step 1: Write failing tests for the new IBatchStateMachine interface**

Replace `tests/MSOSync.EngineTests/BatchStateMachineTests.cs` entirely:

```csharp
using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchStateMachineTests
{
    private static (BatchStateMachine Sm, AppDbContext Db) Create()
    {
        var db = TestDbContext.Create();
        return (new BatchStateMachine(db, new FakeClock()), db);
    }

    private static async Task<SyncOutgoingBatch> AddBatch(AppDbContext db, BatchStatus status)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1,
            NodeId        = "hub",
            ChannelId     = "default",
            Status        = (byte)status
        };
        db.OutgoingBatches.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task MoveToSendingAsync_FromNew_TransitionsAndSetsSentTime()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.New);

        var result = await sm.MoveToSendingAsync(batch.BatchId);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Sending);
        updated.SentTime.Should().NotBeNull();
    }

    [Fact]
    public async Task MoveToSendingAsync_FromError_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Error);

        var result = await sm.MoveToSendingAsync(batch.BatchId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MoveToAcknowledgedAsync_FromSending_SetsAckTime()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Sending);
        var ackTime  = DateTimeOffset.UtcNow;

        var result = await sm.MoveToAcknowledgedAsync(batch.BatchId, ackTime);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
        updated.AckTime.Should().NotBeNull();
    }

    [Fact]
    public async Task MoveToAcknowledgedAsync_FromNew_ReturnsTrueForPullMode()
    {
        // PULL mode: batch stays New until ACK, so New→Acknowledged must be valid
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.New);

        var result = await sm.MoveToAcknowledgedAsync(batch.BatchId, DateTimeOffset.UtcNow);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MoveToErrorAsync_FromSending_Transitions()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Sending);

        var result = await sm.MoveToErrorAsync(batch.BatchId);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Error);
    }

    [Fact]
    public async Task MoveToErrorAsync_FromNew_ReturnsTrueForPullMode()
    {
        // PULL mode: negative ACK → New→Error
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.New);

        var result = await sm.MoveToErrorAsync(batch.BatchId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MoveToRetryAsync_FromError_Transitions()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Error);

        var result = await sm.MoveToRetryAsync(batch.BatchId);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Retry);
    }

    [Fact]
    public async Task MoveToRetryAsync_FromAcknowledged_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Acknowledged);

        var result = await sm.MoveToRetryAsync(batch.BatchId);

        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: compilation errors (IBatchStateMachine still has old API) or test failures.

- [ ] **Step 3: Rename BatchStatus values**

Replace `src/MSOSync.Batch/BatchStatus.cs` entirely:

```csharp
namespace MSOSync.Batch;

public enum BatchStatus : byte
{
    New          = 0,
    Sending      = 1,   // PUSH: HTTP call in-flight; crash → Sending→Error in SchedulerRecovery
    Acknowledged = 2,
    Error        = 3,
    Retry        = 4
}
```

- [ ] **Step 4: Replace IBatchStateMachine interface**

Replace `src/MSOSync.Batch/IBatchStateMachine.cs` entirely:

```csharp
namespace MSOSync.Batch;

public interface IBatchStateMachine
{
    Task<bool> MoveToSendingAsync(long batchId, CancellationToken ct = default);
    Task<bool> MoveToAcknowledgedAsync(long batchId, DateTimeOffset ackTime, CancellationToken ct = default);
    Task<bool> MoveToErrorAsync(long batchId, CancellationToken ct = default);
    Task<bool> MoveToRetryAsync(long batchId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Rewrite BatchStateMachine**

Replace `src/MSOSync.Batch/BatchStateMachine.cs` entirely:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Batch;

public sealed class BatchStateMachine(AppDbContext db, IClock clock) : IBatchStateMachine
{
    // Valid (from, to) transitions
    private static readonly HashSet<(BatchStatus From, BatchStatus To)> ValidTransitions =
    [
        (BatchStatus.New,          BatchStatus.Sending),      // PUSH: start sending
        (BatchStatus.New,          BatchStatus.Acknowledged),  // PULL: ACK success (batch was never moved)
        (BatchStatus.New,          BatchStatus.Error),         // PULL: negative ACK
        (BatchStatus.Sending,      BatchStatus.Acknowledged),  // PUSH: success
        (BatchStatus.Sending,      BatchStatus.Error),         // PUSH: failure / timeout
        (BatchStatus.Error,        BatchStatus.Retry),
        (BatchStatus.Retry,        BatchStatus.Sending),       // PUSH retry
        (BatchStatus.Retry,        BatchStatus.Acknowledged),  // PULL retry → ack
        (BatchStatus.Retry,        BatchStatus.Error),
    ];

    public async Task<bool> MoveToSendingAsync(long batchId, CancellationToken ct = default)
    {
        if (!IsValidFrom(BatchStatus.Sending)) return false;
        var sentTime = clock.UtcNow;

        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId
                     && (b.Status == (byte)BatchStatus.New || b.Status == (byte)BatchStatus.Retry))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status,   (byte)BatchStatus.Sending)
                .SetProperty(b => b.SentTime, sentTime), ct);

        return rows == 1;
    }

    public async Task<bool> MoveToAcknowledgedAsync(
        long batchId, DateTimeOffset ackTime, CancellationToken ct = default)
    {
        var ackUtc = ackTime.UtcDateTime;

        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId
                     && (b.Status == (byte)BatchStatus.New
                      || b.Status == (byte)BatchStatus.Sending
                      || b.Status == (byte)BatchStatus.Retry))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status,  (byte)BatchStatus.Acknowledged)
                .SetProperty(b => b.AckTime, ackUtc), ct);

        return rows == 1;
    }

    public async Task<bool> MoveToErrorAsync(long batchId, CancellationToken ct = default)
    {
        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId
                     && (b.Status == (byte)BatchStatus.New
                      || b.Status == (byte)BatchStatus.Sending
                      || b.Status == (byte)BatchStatus.Retry))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, (byte)BatchStatus.Error), ct);

        return rows == 1;
    }

    public async Task<bool> MoveToRetryAsync(long batchId, CancellationToken ct = default)
    {
        var rows = await db.OutgoingBatches
            .Where(b => b.BatchId == batchId && b.Status == (byte)BatchStatus.Error)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, (byte)BatchStatus.Retry), ct);

        return rows == 1;
    }

    // Helper: checks if any existing status can transition TO the given target
    private static bool IsValidFrom(BatchStatus to) =>
        ValidTransitions.Any(t => t.To == to);
}
```

Note: The `IsValidFrom` helper is unused externally — the valid transition logic is embedded in each method's WHERE clause. The `ValidTransitions` set is kept for documentation and can be used in tests.

- [ ] **Step 6: Update SchedulerRecovery**

The existing code uses `TransitionAsync` (removed) and `BatchStatus.Sent` (renamed to `Sending`). Replace `src/MSOSync.Scheduler/SchedulerRecovery.cs` Phase 1 and Phase 3:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;

namespace MSOSync.Scheduler;

public sealed class SchedulerRecovery(
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerRecovery> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stateMachine = scope.ServiceProvider.GetRequiredService<IBatchStateMachine>();
        var clock        = scope.ServiceProvider.GetRequiredService<IClock>();
        var mediator     = scope.ServiceProvider.GetRequiredService<IMediator>();
        var now          = clock.UtcNow;

        // 1. Sending → Error (crash during PUSH send — will gain sent_time filter in Task 13)
        var sendingBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Sending)
            .ToListAsync(ct);

        var sendingRecovered = 0;
        foreach (var b in sendingBatches)
        {
            if (await stateMachine.MoveToErrorAsync(b.BatchId, ct))
            {
                sendingRecovered++;
                logger.LogInformation("Recovery {Reason}: Batch {BatchId} Sending→Error",
                    RecoveryReason.Restart, b.BatchId);
            }
        }

        // 2. RETRY with overdue next_retry_time → requeue
        var overdueBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.Retry
                     && b.NextRetryTime != null
                     && b.NextRetryTime <= now)
            .ToListAsync(ct);

        var retryRequeued = 0;
        foreach (var b in overdueBatches)
        {
            b.NextRetryTime = null;
            retryRequeued++;
            logger.LogInformation("Recovery {Reason}: Batch {BatchId} overdue retry requeued",
                RecoveryReason.OverdueRetry, b.BatchId);
        }

        if (retryRequeued > 0) await db.SaveChangesAsync(ct);

        // 3. NEW batches older than 10 min — never sent (restart scenario)
        var staleTime = now.AddMinutes(-10);
        var newBatches = await db.OutgoingBatches
            .Where(b => b.Status == (byte)BatchStatus.New && b.CreateTime < staleTime)
            .ToListAsync(ct);

        var newRecovered = 0;
        foreach (var b in newBatches)
        {
            if (await stateMachine.MoveToRetryAsync(b.BatchId, ct))
            {
                newRecovered++;
                logger.LogInformation("Recovery {Reason}: Batch {BatchId} New→Retry",
                    RecoveryReason.Restart, b.BatchId);
            }
        }

        logger.LogInformation(
            "SchedulerRecovery complete: sendingRecovered={S} retryRequeued={R} newRecovered={N}",
            sendingRecovered, retryRequeued, newRecovered);

        await mediator.Publish(
            new SchedulerRecoveryEvent(sendingRecovered, retryRequeued, newRecovered), ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

Note: Phase 3 now uses `MoveToRetryAsync` (stale New batches queue for retry). This is correct — New batches that haven't been sent are queued for retry, not immediately errored.

- [ ] **Step 7: Run tests to verify they pass**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.EngineTests -c Debug
```

Expected: all `BatchStateMachineTests` pass. Other test classes may have references to old `BatchStatus.Sent` / `BatchStatus.Ok` — fix those too (rename to `Sending`/`Acknowledged`).

- [ ] **Step 8: Verify build is clean**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: zero warnings, zero errors.

- [ ] **Step 9: Commit**

```pwsh
git add src/MSOSync.Batch/BatchStatus.cs
git add src/MSOSync.Batch/IBatchStateMachine.cs
git add src/MSOSync.Batch/BatchStateMachine.cs
git add src/MSOSync.Scheduler/SchedulerRecovery.cs
git add tests/MSOSync.EngineTests/BatchStateMachineTests.cs
git commit -m "feat(epic6): BatchStatus Sending/Acknowledged, named IBatchStateMachine methods, IClock injection"
```
