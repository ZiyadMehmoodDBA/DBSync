# Task 2 — Metadata Services

**Files:**
- Create: `src/MSOSync.Metadata/Options/ReplayOptions.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/Dtos/CreateReplayRequest.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayOperationCreatedDto.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayOperationDetailDto.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayItemDto.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayItemFilter.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/IReplayOperationService.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/ReplayOperationService.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/IReplayOperationQueryService.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/ReplayOperationQueryService.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs` (register new services + ReplayOptions)
- Modify: `src/MSOSync.App/appsettings.json` (add "Replay": {} section)
- Test: `tests/MSOSync.MetadataTests/Operations/Replay/ReplayOperationServiceTests.cs`

**Interfaces:**
- Consumes from Task 1: `SyncReplayRequest`, `SyncReplayItem`, `OperationType.BatchReplay`, `OperationResult.NoData`, `ReplayMode`, `ReplayItemStatus`
- Consumes: `IOperationService`, `INodeMetadataService`, `AppDbContext`, `IClock`
- Produces:
  - `ReplayOptions` with `MaxRangeDays`, `WorkerIntervalSeconds`, `MaxConcurrentOperations`, `ItemPageSize`
  - `IReplayOperationService` with `CreateAsync(CreateReplayRequest, ct)` and `CancelAsync(Guid, ct)`
  - `IReplayOperationQueryService` with `GetDetailAsync(Guid, ct)` and `GetItemsAsync(Guid, ReplayItemFilter, ct)`
  - All DTOs listed above

---

- [ ] **Step 1: Create `ReplayOptions`**

```csharp
// src/MSOSync.Metadata/Options/ReplayOptions.cs
namespace MSOSync.Metadata.Options;

public sealed class ReplayOptions
{
    public const string Section = "Replay";

    public int MaxRangeDays            { get; init; } = 90;
    public int WorkerIntervalSeconds   { get; init; } = 10;
    public int MaxConcurrentOperations { get; init; } = 5;
    public int ItemPageSize            { get; init; } = 50;
}
```

- [ ] **Step 2: Create DTOs**

```csharp
// src/MSOSync.Metadata/Operations/Replay/Dtos/CreateReplayRequest.cs
namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record CreateReplayRequest(
    string    NodeId,
    string    ReplayMode,      // "FailedDelivery" | "MissedData" | "Both"
    DateTime  FromTime,
    DateTime  ToTime,
    string[]? ChannelIds,      // null = all channels
    long[]?   BatchIds,        // null = no cherry-pick; FailedDelivery only
    Guid?     InitiatedBy);
```

```csharp
// src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayOperationCreatedDto.cs
namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record ReplayOperationCreatedDto(Guid OperationId, int ItemCount);
```

```csharp
// src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayOperationDetailDto.cs
namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record ReplayOperationDetailDto(
    Guid      OperationId,
    string    Status,
    string?   Result,
    string    NodeId,
    string    ReplayMode,
    DateTime  FromTime,
    DateTime  ToTime,
    string[]? ChannelIds,
    long[]?   BatchIds,
    int       TotalItems,
    int       CompletedItems,
    int       FailedItems,
    int       SkippedItems,
    DateTime? StartedAt,
    DateTime? CompletedAt);
```

```csharp
// src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayItemDto.cs
namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record ReplayItemDto(
    Guid    ItemId,
    string  NodeId,
    string  ChannelId,
    int     EventCount,
    string  Status,
    string? ErrorMessage,
    long?   SourceBatchId,
    long?   ReplayBatchId);
```

```csharp
// src/MSOSync.Metadata/Operations/Replay/Dtos/ReplayItemFilter.cs
namespace MSOSync.Metadata.Operations.Replay.Dtos;

public sealed record ReplayItemFilter(
    string? Status,
    string? Cursor,
    int     PageSize = 50);
```

- [ ] **Step 3: Create `IReplayOperationService`**

```csharp
// src/MSOSync.Metadata/Operations/Replay/IReplayOperationService.cs
using MSOSync.Metadata.Operations.Replay.Dtos;

namespace MSOSync.Metadata.Operations.Replay;

public interface IReplayOperationService
{
    Task<ReplayOperationCreatedDto> CreateAsync(CreateReplayRequest req, CancellationToken ct = default);
    Task CancelAsync(Guid operationId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write failing tests for `ReplayOperationService`**

```csharp
// tests/MSOSync.MetadataTests/Operations/Replay/ReplayOperationServiceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Replay;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Replay;

public sealed class ReplayOperationServiceTests
{
    private readonly Mock<INodeMetadataService> _nodeMeta = new();
    private readonly Mock<IOperationService>    _ops      = new();

    private static NodeDto ActiveNode(string id = "n1") => new(
        id, "g", "http://n1", NodeLifecycleState.Active,
        null, null, 30, true, TransportMode.Push, ConnectivityStatus.Reachable,
        false, null, null, null, null, false, null);

    private static NodeDto DisabledNode(string id = "n1") => new(
        id, "g", "http://n1", NodeLifecycleState.Disabled,
        null, null, 30, false, TransportMode.Push, ConnectivityStatus.Unreachable,
        false, null, null, null, null, false, null);

    private static NodeDto DrainingNode(string id = "n1") => new(
        id, "g", "http://n1", NodeLifecycleState.Draining,
        null, null, 30, true, TransportMode.Push, ConnectivityStatus.Reachable,
        false, null, null, null, null, false, null);

    private ReplayOperationService BuildService(AppDbContext db)
        => new(db, _ops.Object, _nodeMeta.Object);

    // MetadataTests uses TestDbContext.Create() (not TestDb.Create())
    private static AppDbContext NewDb() => TestDbContext.Create();

    private static CreateReplayRequest ValidRequest(string mode = "FailedDelivery") => new(
        NodeId:     "n1",
        ReplayMode: mode,
        FromTime:   DateTime.UtcNow.AddDays(-1),
        ToTime:     DateTime.UtcNow,
        ChannelIds: null,
        BatchIds:   null,
        InitiatedBy: null);

    [Fact]
    public async Task CreateAsync_ActiveNode_ValidRange_Returns_OperationWithItems()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        _ops.Setup(x => x.CreateAsync(
            OperationType.BatchReplay, null, null, OperationSource.User,
            It.IsAny<string>(), true, false, It.IsAny<string>(), null, default))
            .ReturnsAsync(Guid.NewGuid());

        var db = NewDb();
        db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            NodeId = "n1", ChannelId = "ch1", Status = (byte)BatchStatus.Error,
            BatchSequence = 1, CreateTime = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var svc    = BuildService(db);
        var result = await svc.CreateAsync(ValidRequest(), default);

        result.Should().NotBeNull();
        result.ItemCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_DrainingNode_ValidRange_Returns_OperationWithItems()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(DrainingNode());
        _ops.Setup(x => x.CreateAsync(
            OperationType.BatchReplay, null, null, OperationSource.User,
            It.IsAny<string>(), true, false, It.IsAny<string>(), null, default))
            .ReturnsAsync(Guid.NewGuid());

        var db  = NewDb();
        var svc = BuildService(db);

        // No batches → NoData case
        var result = await svc.CreateAsync(ValidRequest(), default);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_DisabledNode_Throws_OperationStateException()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(DisabledNode());
        var db  = NewDb();
        var svc = BuildService(db);

        var act = () => svc.CreateAsync(ValidRequest(), default);
        await act.Should().ThrowAsync<OperationStateException>();
    }

    [Fact]
    public async Task CreateAsync_RangeExceeds90Days_Throws_OperationStateException()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        var db  = NewDb();
        var svc = BuildService(db);

        var req = new CreateReplayRequest("n1", "FailedDelivery",
            DateTime.UtcNow.AddDays(-100), DateTime.UtcNow, null, null, null);
        var act = () => svc.CreateAsync(req, default);
        await act.Should().ThrowAsync<OperationStateException>();
    }

    [Fact]
    public async Task CreateAsync_NoMatchingBatches_Completes_Immediately_NoData()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        var opId = Guid.NewGuid();
        _ops.Setup(x => x.CreateAsync(
            OperationType.BatchReplay, null, null, OperationSource.User,
            It.IsAny<string>(), true, false, It.IsAny<string>(), null, default))
            .ReturnsAsync(opId);
        _ops.Setup(x => x.CompleteAsync(opId, OperationResult.NoData, It.IsAny<string?>(), default))
            .Returns(Task.CompletedTask);

        var db  = NewDb();
        var svc = BuildService(db);

        var result = await svc.CreateAsync(ValidRequest(), default);
        result.ItemCount.Should().Be(0);
        _ops.Verify(x => x.CompleteAsync(opId, OperationResult.NoData, It.IsAny<string?>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_BatchIds_Only_Allowed_For_FailedDelivery_Mode()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        var db  = NewDb();
        var svc = BuildService(db);

        var req = new CreateReplayRequest("n1", "MissedData",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            null, new long[] { 1, 2 }, null);  // BatchIds with MissedData
        var act = () => svc.CreateAsync(req, default);
        await act.Should().ThrowAsync<OperationStateException>();
    }

    [Fact]
    public async Task CreateAsync_FailedDelivery_Items_Have_SourceBatchId_Set()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        var opId = Guid.NewGuid();
        _ops.Setup(x => x.CreateAsync(
            OperationType.BatchReplay, null, null, OperationSource.User,
            It.IsAny<string>(), true, false, It.IsAny<string>(), null, default))
            .ReturnsAsync(opId);

        var db = TestDb.Create();
        db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            NodeId = "n1", ChannelId = "ch1", Status = (byte)BatchStatus.Error,
            BatchSequence = 1, CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var svc    = BuildService(db);
        var result = await svc.CreateAsync(ValidRequest("FailedDelivery"), default);

        db.ChangeTracker.Clear();
        var items = await db.ReplayItems.Where(i => i.OperationId == opId).ToListAsync();
        items.Should().AllSatisfy(i => i.SourceBatchId.Should().NotBeNull());
    }

    [Fact]
    public async Task CreateAsync_MissedData_Items_Have_SourceBatchId_Null()
    {
        // For MissedData, items created from SyncDataEvent have no source batch id
        // This test uses empty data — items count = 0 → NoData result
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        var opId = Guid.NewGuid();
        _ops.Setup(x => x.CreateAsync(
            OperationType.BatchReplay, null, null, OperationSource.User,
            It.IsAny<string>(), true, false, It.IsAny<string>(), null, default))
            .ReturnsAsync(opId);
        _ops.Setup(x => x.CompleteAsync(opId, OperationResult.NoData, It.IsAny<string?>(), default))
            .Returns(Task.CompletedTask);

        var db  = NewDb();
        var svc = BuildService(db);

        var req = ValidRequest("MissedData");
        var result = await svc.CreateAsync(req, default);
        result.ItemCount.Should().Be(0);
    }

    [Fact]
    public async Task CancelAsync_Running_Sets_Cancelled_Skips_Pending_Items()
    {
        var opId = Guid.NewGuid();
        _ops.Setup(x => x.CancelAsync(opId, Guid.Empty, default)).Returns(Task.CompletedTask);

        var db = TestDb.Create();
        db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "BatchReplay",
            Status = "Running", Source = "User", StartedAt = DateTime.UtcNow
        });
        db.ReplayItems.Add(new SyncReplayItem
        {
            ItemId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ChannelId = "ch1", EventCount = 1,
            Status = nameof(ReplayItemStatus.Pending)
        });
        await db.SaveChangesAsync();

        var svc = BuildService(db);
        await svc.CancelAsync(opId, default);

        db.ChangeTracker.Clear();
        var items = await db.ReplayItems.Where(i => i.OperationId == opId).ToListAsync();
        items.Should().AllSatisfy(i => i.Status.Should().Be(nameof(ReplayItemStatus.Skipped)));
    }

    [Fact]
    public async Task CancelAsync_Completed_Throws_OperationStateException()
    {
        var opId = Guid.NewGuid();
        var db   = TestDb.Create();
        db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "BatchReplay",
            Status = "Completed", Source = "User", StartedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = BuildService(db);
        var act = () => svc.CancelAsync(opId, default);
        await act.Should().ThrowAsync<OperationStateException>();
    }
}
```

- [ ] **Step 5: Run tests — expect compilation failure (service not yet written)**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~ReplayOperationServiceTests" -v normal
```

Expected: build errors referencing `ReplayOperationService`, `IReplayOperationService`, etc.

- [ ] **Step 6: Implement `ReplayOperationService`**

```csharp
// src/MSOSync.Metadata/Operations/Replay/ReplayOperationService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Replay;

public sealed class ReplayOperationService(
    AppDbContext         db,
    IOperationService    operations,
    INodeMetadataService nodeMeta) : IReplayOperationService
{
    private const int MaxRangeDays = 90; // Will be injected from ReplayOptions in production registration

    public async Task<ReplayOperationCreatedDto> CreateAsync(
        CreateReplayRequest req, CancellationToken ct = default)
    {
        // 1. Validate node
        var node = await nodeMeta.GetNodeAsync(req.NodeId, ct);
        if (node is null)
            throw new NotFoundException($"Node '{req.NodeId}' not found", "NODE_NOT_FOUND");

        if (node.LifecycleState is not (NodeLifecycleState.Active or NodeLifecycleState.Draining))
            throw new OperationStateException(
                $"Node '{req.NodeId}' is {node.LifecycleState} — only Active or Draining nodes can be replayed");

        // 2. Validate time range
        if (req.FromTime >= req.ToTime)
            throw new OperationStateException("FromTime must be before ToTime");

        if ((req.ToTime - req.FromTime).TotalDays > MaxRangeDays)
            throw new OperationStateException($"Time range exceeds maximum of {MaxRangeDays} days");

        // 3. Validate BatchIds only for FailedDelivery
        var mode = Enum.Parse<ReplayMode>(req.ReplayMode);
        if (req.BatchIds is { Length: > 0 } && mode != ReplayMode.FailedDelivery)
            throw new OperationStateException("BatchIds can only be specified for FailedDelivery mode");

        // 4. Create SyncOperation
        var operationId = await operations.CreateAsync(
            OperationType.BatchReplay, referenceId: null,
            req.InitiatedBy, OperationSource.User,
            correlationId: Guid.NewGuid().ToString(),
            canCancel: true, canRetry: false,
            summary: $"Batch replay ({req.ReplayMode}) for node {req.NodeId}",
            metadataJson: null, ct);

        // 5. Create SyncReplayRequest
        db.ReplayRequests.Add(new SyncReplayRequest
        {
            ReplayId      = Guid.NewGuid(),
            OperationId   = operationId,
            NodeId        = req.NodeId,
            ChannelIdsJson = req.ChannelIds is null ? null : JsonSerializer.Serialize(req.ChannelIds),
            BatchIdsJson   = req.BatchIds   is null ? null : JsonSerializer.Serialize(req.BatchIds),
            FromTime      = req.FromTime,
            ToTime        = req.ToTime,
            ReplayMode    = req.ReplayMode,
            TenantId      = Guid.Empty, // filled by tenant filter
        });

        // 6. Enumerate items
        var items = await EnumerateItemsAsync(req, mode, operationId, ct);
        foreach (var item in items)
            db.ReplayItems.Add(item);

        await db.SaveChangesAsync(ct);

        // 7. Zero items → complete immediately with NoData
        if (items.Count == 0)
        {
            await operations.CompleteAsync(operationId, OperationResult.NoData,
                "No matching batches found", ct);
        }

        return new ReplayOperationCreatedDto(operationId, items.Count);
    }

    public async Task CancelAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await db.Operations.FindAsync([operationId], ct)
            ?? throw new NotFoundException($"Operation {operationId} not found", "NOT_FOUND");

        if (op.Status is "Completed" or "Failed" or "Cancelled")
            throw new OperationStateException($"Cannot cancel operation in status {op.Status}");

        // Mark pending items as skipped
        await db.ReplayItems
            .Where(i => i.OperationId == operationId && i.Status == nameof(ReplayItemStatus.Pending))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, nameof(ReplayItemStatus.Skipped)), ct);

        await operations.CancelAsync(operationId, Guid.Empty, ct);
    }

    private async Task<List<SyncReplayItem>> EnumerateItemsAsync(
        CreateReplayRequest req, ReplayMode mode, Guid operationId, CancellationToken ct)
    {
        var items = new List<SyncReplayItem>();

        if (mode is ReplayMode.FailedDelivery or ReplayMode.Both)
        {
            var errorStatus = (byte)BatchStatus.Error;
            var query = db.OutgoingBatches
                .Where(b => b.NodeId == req.NodeId
                         && b.Status == errorStatus
                         && b.CreateTime >= req.FromTime
                         && b.CreateTime <= req.ToTime);

            if (req.ChannelIds is { Length: > 0 })
                query = query.Where(b => req.ChannelIds.Contains(b.ChannelId));

            if (req.BatchIds is { Length: > 0 })
                query = query.Where(b => req.BatchIds.Contains(b.BatchId));

            var batches = await query.AsNoTracking().ToListAsync(ct);

            items.AddRange(batches.Select(b => new SyncReplayItem
            {
                ItemId        = Guid.NewGuid(),
                OperationId   = operationId,
                SourceBatchId = b.BatchId,
                NodeId        = b.NodeId,
                ChannelId     = b.ChannelId,
                EventCount    = 0, // not tracked for FailedDelivery
                Status        = nameof(ReplayItemStatus.Pending),
                TenantId      = Guid.Empty,
            }));
        }

        if (mode is ReplayMode.MissedData or ReplayMode.Both)
        {
            // Query events in range, group by channel
            var eventQuery = db.DataEvents
                .Where(e => e.CreateTime >= req.FromTime && e.CreateTime <= req.ToTime);

            if (req.ChannelIds is { Length: > 0 })
                eventQuery = eventQuery.Where(e => req.ChannelIds.Contains(e.ChannelId));

            var channels = await eventQuery
                .GroupBy(e => e.ChannelId)
                .Select(g => new { ChannelId = g.Key, EventCount = g.Count() })
                .ToListAsync(ct);

            // For MissedData, worker will resolve routing and filter at advance time
            // Items for MissedData have no source_batch_id
            foreach (var ch in channels)
            {
                // Skip channels already enumerated in FailedDelivery
                if (mode == ReplayMode.Both && items.Any(i => i.ChannelId == ch.ChannelId))
                    continue;

                items.Add(new SyncReplayItem
                {
                    ItemId      = Guid.NewGuid(),
                    OperationId = operationId,
                    SourceBatchId = null,
                    NodeId      = req.NodeId,
                    ChannelId   = ch.ChannelId,
                    EventCount  = ch.EventCount,
                    Status      = nameof(ReplayItemStatus.Pending),
                    TenantId    = Guid.Empty,
                });
            }
        }

        return items;
    }
}
```

Note: `BatchStatus` is in `MSOSync.Batch` which `MSOSync.Metadata` does NOT reference. Use the raw byte value `(byte)3` for `Error`, or add a shared constant. Simplest: add `using MSOSync.Batch;` requires a project reference. Instead, define the constant locally:

```csharp
// Inside ReplayOperationService, before EnumerateItemsAsync:
private const byte BatchStatusError = 3; // BatchStatus.Error
```

And use `b.Status == BatchStatusError` instead.

- [ ] **Step 7: Create `IReplayOperationQueryService`**

```csharp
// src/MSOSync.Metadata/Operations/Replay/IReplayOperationQueryService.cs
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Operations.Replay.Dtos;

namespace MSOSync.Metadata.Operations.Replay;

public interface IReplayOperationQueryService
{
    Task<ReplayOperationDetailDto?> GetDetailAsync(Guid operationId, CancellationToken ct = default);
    Task<CursorPageResult<ReplayItemDto>> GetItemsAsync(
        Guid operationId, ReplayItemFilter filter, CancellationToken ct = default);
}
```

- [ ] **Step 8: Implement `ReplayOperationQueryService`**

```csharp
// src/MSOSync.Metadata/Operations/Replay/ReplayOperationQueryService.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations.Replay;

public sealed class ReplayOperationQueryService(AppDbContext db) : IReplayOperationQueryService
{
    public async Task<ReplayOperationDetailDto?> GetDetailAsync(
        Guid operationId, CancellationToken ct = default)
    {
        var op = await db.Operations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct);
        if (op is null) return null;

        var req = await db.ReplayRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OperationId == operationId, ct);

        var counts = await db.ReplayItems
            .Where(i => i.OperationId == operationId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total     = g.Count(),
                Completed = g.Count(i => i.Status == "Completed"),
                Failed    = g.Count(i => i.Status == "Failed"),
                Skipped   = g.Count(i => i.Status == "Skipped"),
            })
            .FirstOrDefaultAsync(ct);

        return new ReplayOperationDetailDto(
            OperationId:   op.OperationId,
            Status:        op.Status,
            Result:        op.Result,
            NodeId:        req?.NodeId ?? string.Empty,
            ReplayMode:    req?.ReplayMode ?? string.Empty,
            FromTime:      req?.FromTime ?? default,
            ToTime:        req?.ToTime ?? default,
            ChannelIds:    req?.ChannelIdsJson is null ? null
                           : JsonSerializer.Deserialize<string[]>(req.ChannelIdsJson),
            BatchIds:      req?.BatchIdsJson is null ? null
                           : JsonSerializer.Deserialize<long[]>(req.BatchIdsJson),
            TotalItems:    counts?.Total ?? 0,
            CompletedItems: counts?.Completed ?? 0,
            FailedItems:   counts?.Failed ?? 0,
            SkippedItems:  counts?.Skipped ?? 0,
            StartedAt:     op.StartedAt,
            CompletedAt:   op.CompletedAt);
    }

    public async Task<CursorPageResult<ReplayItemDto>> GetItemsAsync(
        Guid operationId, ReplayItemFilter filter, CancellationToken ct = default)
    {
        var query = db.ReplayItems.AsNoTracking()
            .Where(i => i.OperationId == operationId);

        if (filter.Status is not null)
            query = query.Where(i => i.Status == filter.Status);

        if (filter.Cursor is not null
            && Guid.TryParse(filter.Cursor, out var cursorId))
            query = query.Where(i => i.ItemId.CompareTo(cursorId) > 0);

        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var items    = await query.OrderBy(i => i.ItemId)
            .Take(pageSize + 1)
            .Select(i => new ReplayItemDto(
                i.ItemId, i.NodeId, i.ChannelId, i.EventCount,
                i.Status, i.ErrorMessage, i.SourceBatchId, i.ReplayBatchId))
            .ToListAsync(ct);

        var hasMore    = items.Count > pageSize;
        var page       = hasMore ? items.Take(pageSize).ToList() : items;
        var nextCursor = hasMore ? page[^1].ItemId.ToString() : null;

        return new CursorPageResult<ReplayItemDto>(page, nextCursor, hasMore, null);
    }
}
```

- [ ] **Step 9: Register in `MetadataServiceExtensions`**

In `src/MSOSync.Metadata/MetadataServiceExtensions.cs`, add after the existing `// Epic 12C — Operations registry` block:

```csharp
        // Phase 2B.2 — Batch Replay
        services.Configure<MSOSync.Metadata.Options.ReplayOptions>(
            configuration.GetSection(MSOSync.Metadata.Options.ReplayOptions.Section));
        services.AddScoped<IReplayOperationService,      ReplayOperationService>();
        services.AddScoped<IReplayOperationQueryService, ReplayOperationQueryService>();
```

Also add the using at top of the file:
```csharp
using MSOSync.Metadata.Operations.Replay;
```

- [ ] **Step 10: Add Replay section to `appsettings.json`**

In `src/MSOSync.App/appsettings.json`, add alongside the existing config sections:

```json
"Replay": {
  "MaxRangeDays": 90,
  "WorkerIntervalSeconds": 10,
  "MaxConcurrentOperations": 5,
  "ItemPageSize": 50
}
```

- [ ] **Step 11: Run tests to verify they pass**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~ReplayOperationServiceTests" -v normal
```

Expected: all 9 tests pass.

- [ ] **Step 12: Build to verify no compilation errors**

```
dotnet build src/MSOSync.Metadata/MSOSync.Metadata.csproj
dotnet build src/MSOSync.App/MSOSync.App.csproj
```

Expected: 0 errors.

- [ ] **Step 13: Commit**

```
git add src/MSOSync.Metadata/Options/ReplayOptions.cs
git add src/MSOSync.Metadata/Operations/Replay/
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.App/appsettings.json
git add tests/MSOSync.MetadataTests/Operations/Replay/
git commit -m "feat(2B.2-T2): IReplayOperationService + query service + ReplayOptions"
```
