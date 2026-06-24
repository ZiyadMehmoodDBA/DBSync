# Task 4: IncomingBatchQueryService

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Implement `IIncomingBatchQueryService` with list + detail queries. All fields come directly from `SyncIncomingBatch` — no joins needed. `ApplyTimeMs` in detail query: use stored value if present; compute from `(AppliedTime - ReceivedTime).TotalMilliseconds` in C# if stored value is null (avoids SQL Server-specific `DateDiffMillisecond`).

**Files:**
- Create: `src/MSOSync.Metadata/IncomingBatches/IIncomingBatchQueryService.cs`
- Create: `src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs`
- Create: `tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchQueryServiceTests.cs`

**Interfaces:**
- Consumes: `IncomingBatchFilter`, `IncomingBatchSummaryDto`, `IncomingBatchDetailDto`, `PagedResult<T>` (Task 1); `AppDbContext` (Persistence)
- Produces:
  - `IIncomingBatchQueryService.GetIncomingBatchesAsync(IncomingBatchFilter filter, CancellationToken ct) → Task<PagedResult<IncomingBatchSummaryDto>>`
  - `IIncomingBatchQueryService.GetIncomingBatchByIdAsync(long batchId, CancellationToken ct) → Task<IncomingBatchDetailDto?>`

---

- [ ] **Step 1: Write failing tests**

Create `tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchQueryServiceTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.IncomingBatches;

public sealed class IncomingBatchQueryServiceTests
{
    private static (IncomingBatchQueryService Svc, AppDbContext Db) Make()
    {
        var db  = TestDbContext.Create();
        var svc = new IncomingBatchQueryService(db);
        return (svc, db);
    }

    private static SyncNode MakeNode(string nodeId) => new()
    {
        NodeId          = nodeId,
        NodeName        = nodeId,
        NodeType        = "HUB",
        Status          = "REGISTERED",
        IsEnabled       = true,
        DatabaseType    = "SQLSERVER",
        SyncUrl         = "http://localhost",
        CreatedTime     = DateTime.UtcNow
    };

    private static SyncIncomingBatch MakeBatch(string sourceNodeId, IncomingBatchStatus status,
        long batchId) => new()
    {
        BatchId       = batchId,
        NodeId        = sourceNodeId,
        SourceNodeId  = sourceNodeId,
        ChannelId     = "ch-1",
        Status        = status,
        BatchSequence = batchId,
        ReceivedTime  = DateTime.UtcNow
    };

    [Fact]
    public async Task GetIncomingBatchesAsync_NoFilter_ReturnsAll()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        db.IncomingBatches.AddRange(
            MakeBatch("node-1", IncomingBatchStatus.Applied, 1L),
            MakeBatch("node-1", IncomingBatchStatus.Error,   2L));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(new IncomingBatchFilter(), default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetIncomingBatchesAsync_FilterByStatus_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        db.IncomingBatches.AddRange(
            MakeBatch("node-1", IncomingBatchStatus.Applied, 1L),
            MakeBatch("node-1", IncomingBatchStatus.Error,   2L),
            MakeBatch("node-1", IncomingBatchStatus.New,     3L));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(
            new IncomingBatchFilter { Status = IncomingBatchStatus.Error }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().Status.Should().Be(IncomingBatchStatus.Error);
    }

    [Fact]
    public async Task GetIncomingBatchesAsync_FilterBySourceNode_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.Nodes.AddRange(MakeNode("node-1"), MakeNode("node-2"));
        await db.SaveChangesAsync();
        db.IncomingBatches.AddRange(
            MakeBatch("node-1", IncomingBatchStatus.Applied, 1L),
            MakeBatch("node-2", IncomingBatchStatus.Applied, 2L));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(
            new IncomingBatchFilter { SourceNodeId = "node-1" }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().SourceNodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task GetIncomingBatchesAsync_Pagination_HonorsPageSize()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        for (long i = 1; i <= 8; i++)
            db.IncomingBatches.Add(MakeBatch("node-1", IncomingBatchStatus.Applied, i));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(
            new IncomingBatchFilter { Page = 2, PageSize = 3 }, default);

        result.TotalCount.Should().Be(8);
        result.Items.Should().HaveCount(3);
        result.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetIncomingBatchByIdAsync_Exists_ReturnsDetailDto()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        var batch = MakeBatch("node-1", IncomingBatchStatus.Applied, 42L);
        batch.AppliedTime = batch.ReceivedTime.AddMilliseconds(250);
        db.IncomingBatches.Add(batch);
        await db.SaveChangesAsync();

        var dto = await svc.GetIncomingBatchByIdAsync(42L, default);

        dto.Should().NotBeNull();
        dto!.BatchId.Should().Be(42L);
        dto.Status.Should().Be(IncomingBatchStatus.Applied);
        dto.ApplyTimeMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetIncomingBatchByIdAsync_Missing_ReturnsNull()
    {
        var (svc, db) = Make();
        var dto = await svc.GetIncomingBatchByIdAsync(99999L, default);
        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetIncomingBatchByIdAsync_NoAppliedTime_ApplyTimeMsNull()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        db.IncomingBatches.Add(MakeBatch("node-1", IncomingBatchStatus.New, 10L));
        await db.SaveChangesAsync();

        var dto = await svc.GetIncomingBatchByIdAsync(10L, default);

        dto!.ApplyTimeMs.Should().BeNull();
    }
}
```

**Note:** `SyncIncomingBatch` has a FK to `SyncNode` via `SourceNodeId`. SQLite enforces FK constraints when enabled. The `MakeNode` helper seeds a required node. `TestAppDbContext` clears column types but FK constraints still apply if SQLite FK enforcement is on. If tests fail due to FK violations, seed nodes first (as shown).

- [ ] **Step 2: Run test to verify it fails (compile error)**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build tests\MSOSync.MetadataTests -c Debug
```

Expected: compile error — `IncomingBatchQueryService`, `IIncomingBatchQueryService` not found.

- [ ] **Step 3: Create interface**

Create `src/MSOSync.Metadata/IncomingBatches/IIncomingBatchQueryService.cs`:

```csharp
using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.IncomingBatches;

public interface IIncomingBatchQueryService
{
    Task<PagedResult<IncomingBatchSummaryDto>> GetIncomingBatchesAsync(
        IncomingBatchFilter filter, CancellationToken ct = default);

    Task<IncomingBatchDetailDto?> GetIncomingBatchByIdAsync(
        long batchId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement IncomingBatchQueryService**

Create `src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;

namespace MSOSync.Metadata.IncomingBatches;

public sealed class IncomingBatchQueryService(AppDbContext db) : IIncomingBatchQueryService
{
    public async Task<PagedResult<IncomingBatchSummaryDto>> GetIncomingBatchesAsync(
        IncomingBatchFilter filter, CancellationToken ct = default)
    {
        var q = db.IncomingBatches.AsNoTracking();

        if (filter.SourceNodeId is not null) q = q.Where(b => b.SourceNodeId == filter.SourceNodeId);
        if (filter.ChannelId    is not null) q = q.Where(b => b.ChannelId    == filter.ChannelId);
        if (filter.Status       is not null) q = q.Where(b => b.Status       == filter.Status);
        if (filter.From         is not null) q = q.Where(b => b.ReceivedTime >= filter.From);
        if (filter.To           is not null) q = q.Where(b => b.ReceivedTime <= filter.To);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(b => b.ReceivedTime)
            .Select(b => new IncomingBatchSummaryDto(
                b.BatchId,
                b.SourceNodeId,
                b.ChannelId,
                b.Status,
                b.RowCount,
                b.BatchSequence,
                b.ReceivedTime,
                b.ApplyTimeMs))
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedResult<IncomingBatchSummaryDto>(
            items.AsReadOnly(), filter.Page, filter.PageSize, total);
    }

    public async Task<IncomingBatchDetailDto?> GetIncomingBatchByIdAsync(
        long batchId, CancellationToken ct = default)
    {
        var b = await db.IncomingBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BatchId == batchId, ct);

        if (b is null) return null;

        var applyTimeMs = b.ApplyTimeMs
            ?? (b.AppliedTime.HasValue
                ? (long)(b.AppliedTime.Value - b.ReceivedTime).TotalMilliseconds
                : (long?)null);

        return new IncomingBatchDetailDto(
            b.BatchId, b.SourceNodeId, b.ChannelId, b.Status,
            b.RowCount, b.BatchSequence, b.ReceivedTime,
            b.LoadTime, b.ExtractTime, b.AppliedTime, applyTimeMs);
    }
}
```

- [ ] **Step 5: Run tests**

```powershell
dotnet test tests\MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~IncomingBatchQueryService" --logger "console;verbosity=normal"
```

Expected: all 7 tests PASS.

- [ ] **Step 6: Verify build**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Metadata/IncomingBatches/IIncomingBatchQueryService.cs
git add src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs
git add tests/MSOSync.MetadataTests/IncomingBatches/IncomingBatchQueryServiceTests.cs
git commit -m "feat(9a): add IIncomingBatchQueryService + IncomingBatchQueryService with SQLite unit tests"
```
