# Task 5: IBatchTransportQueryService + BatchTransportQueryService

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 5 (IBatchTransportQueryService)
**Depends on:** Tasks 1 (entities), 2 (BatchStatus)

**Files:**
- Create: `src/MSOSync.Batch/IBatchTransportQueryService.cs`
- Create: `src/MSOSync.Batch/BatchTransportQueryService.cs`

**Architectural note:** The spec places the interface in `MSOSync.Transport`, but `MSOSync.Transport` already references `MSOSync.Batch` (for `IBatchStateMachine`). Placing the interface in Transport AND having Batch implement it creates a circular project reference. The interface lives in `MSOSync.Batch` alongside `IBatchStateMachine`. Transport references Batch and uses both interfaces through that single project reference. The spec's intent (Transport is persistence-free) is fully preserved.

**Interfaces:**
- Produces: `IBatchTransportQueryService` with 5 query methods; `BatchTransportQueryService` implementation
- Consumed by: Tasks 6, 7, 8, 9, 10, 11, 12

---

- [ ] **Step 1: Write failing tests**

Add to `tests/MSOSync.EngineTests/BatchTransportQueryServiceTests.cs` (new file):
```csharp
using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchTransportQueryServiceTests
{
    private static (BatchTransportQueryService Svc, AppDbContext Db) Create()
    {
        var db = TestDbContext.Create();
        return (new BatchTransportQueryService(db), db);
    }

    [Fact]
    public async Task GetNextPullBatchAsync_NoBatches_ReturnsNull()
    {
        var (svc, _) = Create();
        var (batch, more) = await svc.GetNextPullBatchAsync("hub", "default", 0);
        batch.Should().BeNull();
        more.Should().BeFalse();
    }

    [Fact]
    public async Task GetNextPullBatchAsync_OneBatch_ReturnsBatchNoMore()
    {
        var (svc, db) = Create();
        db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = "hub", ChannelId = "default",
            Status = (byte)BatchStatus.New, RowCount = 5
        });
        await db.SaveChangesAsync();

        var (batch, more) = await svc.GetNextPullBatchAsync("hub", "default", 0);
        batch.Should().NotBeNull();
        more.Should().BeFalse();
    }

    [Fact]
    public async Task GetNextPullBatchAsync_TwoBatches_MoreAvailableTrue()
    {
        var (svc, db) = Create();
        for (var i = 1; i <= 2; i++)
            db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                BatchSequence = i, NodeId = "hub", ChannelId = "default",
                Status = (byte)BatchStatus.New, RowCount = 1
            });
        await db.SaveChangesAsync();

        var (batch, more) = await svc.GetNextPullBatchAsync("hub", "default", 0);
        batch.Should().NotBeNull();
        more.Should().BeTrue();
    }

    [Fact]
    public async Task GetLastSequenceAsync_NoIncoming_ReturnsZero()
    {
        var (svc, _) = Create();
        var seq = await svc.GetLastSequenceAsync("source1", "default");
        seq.Should().Be(0L);
    }

    [Fact]
    public async Task IncomingBatchExistsAsync_NonExistent_ReturnsFalse()
    {
        var (svc, _) = Create();
        var exists = await svc.IncomingBatchExistsAsync("source1", 99L);
        exists.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to see them fail**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.EngineTests -c Debug --filter "BatchTransportQuery"
```

Expected: compilation error (IBatchTransportQueryService not defined yet).

- [ ] **Step 3: Create IBatchTransportQueryService**

Create `src/MSOSync.Batch/IBatchTransportQueryService.cs`:
```csharp
using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public interface IBatchTransportQueryService
{
    /// <summary>
    /// Returns the next New/Retry outgoing batch for the given target node and channel
    /// with sequence > afterSequence, plus whether more batches exist.
    /// Uses Take(2) probe: MoreAvailable = true if there is a batch after this one.
    /// </summary>
    Task<(SyncOutgoingBatch? Batch, bool MoreAvailable)> GetNextPullBatchAsync(
        string targetNodeId, string channelId, long afterSequence,
        CancellationToken ct = default);

    Task<IReadOnlyList<SyncDataEvent>> GetEventsForBatchAsync(
        long batchId, CancellationToken ct = default);

    /// <summary>
    /// Returns the maximum batch_sequence for (sourceNodeId, channelId) in sync_incoming_batch,
    /// or 0 if no batches exist yet.
    /// </summary>
    Task<long> GetLastSequenceAsync(
        string sourceNodeId, string channelId,
        CancellationToken ct = default);

    Task<bool> IncomingBatchExistsAsync(
        string sourceNodeId, long batchSequence,
        CancellationToken ct = default);

    Task InsertIncomingBatchAsync(
        SyncIncomingBatch batch, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement BatchTransportQueryService**

Create `src/MSOSync.Batch/BatchTransportQueryService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Batch;

public sealed class BatchTransportQueryService(AppDbContext db) : IBatchTransportQueryService
{
    public async Task<(SyncOutgoingBatch? Batch, bool MoreAvailable)> GetNextPullBatchAsync(
        string targetNodeId, string channelId, long afterSequence, CancellationToken ct = default)
    {
        // Take 2: first is the batch to serve, second (if exists) means MoreAvailable = true
        var candidates = await db.OutgoingBatches
            .Where(b => b.NodeId        == targetNodeId
                     && b.ChannelId     == channelId
                     && b.BatchSequence > afterSequence
                     && (b.Status == (byte)BatchStatus.New || b.Status == (byte)BatchStatus.Retry))
            .OrderBy(b => b.BatchSequence)
            .Take(2)
            .AsNoTracking()
            .ToListAsync(ct);

        if (candidates.Count == 0) return (null, false);
        return (candidates[0], candidates.Count > 1);
    }

    public async Task<IReadOnlyList<SyncDataEvent>> GetEventsForBatchAsync(
        long batchId, CancellationToken ct = default)
    {
        // DataEventBatches links events to outgoing batches
        var eventIds = await db.DataEventBatches
            .Where(deb => deb.BatchId == batchId)
            .Select(deb => deb.EventId)
            .ToListAsync(ct);

        if (eventIds.Count == 0) return [];

        return await db.DataEvents
            .Where(e => eventIds.Contains(e.EventId))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<long> GetLastSequenceAsync(
        string sourceNodeId, string channelId, CancellationToken ct = default)
    {
        var max = await db.IncomingBatches
            .Where(b => b.SourceNodeId == sourceNodeId && b.ChannelId == channelId)
            .Select(b => (long?)b.BatchSequence)
            .MaxAsync(ct);

        return max ?? 0L;
    }

    public async Task<bool> IncomingBatchExistsAsync(
        string sourceNodeId, long batchSequence, CancellationToken ct = default)
    {
        return await db.IncomingBatches
            .AnyAsync(b => b.SourceNodeId == sourceNodeId && b.BatchSequence == batchSequence, ct);
    }

    public async Task InsertIncomingBatchAsync(SyncIncomingBatch batch, CancellationToken ct = default)
    {
        db.IncomingBatches.Add(batch);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Run tests**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.EngineTests -c Debug --filter "BatchTransportQuery"
```

Expected: all 5 tests pass.

Also check `SyncDataEventBatch` entity has `BatchId` and `EventId` properties (it does — verified from project listing).

- [ ] **Step 6: Build clean**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
```

- [ ] **Step 7: Commit**

```pwsh
git add src/MSOSync.Batch/IBatchTransportQueryService.cs
git add src/MSOSync.Batch/BatchTransportQueryService.cs
git add tests/MSOSync.EngineTests/BatchTransportQueryServiceTests.cs
git commit -m "feat(epic6): IBatchTransportQueryService + BatchTransportQueryService for pull/push queries"
```
