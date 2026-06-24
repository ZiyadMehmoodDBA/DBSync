# Task 3: EventQueryService

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Implement `IEventQueryService` with list + detail queries using `AsNoTracking()` + LINQ projection. The list query returns `PagedResult<EventSummaryDto>`. `BatchId` comes from a correlated MAX subquery on `DataEventBatches`. Unit tests use SQLite via `TestDbContext.Create()`.

**Files:**
- Create: `src/MSOSync.Metadata/Events/IEventQueryService.cs`
- Create: `src/MSOSync.Metadata/Events/EventQueryService.cs`
- Create: `tests/MSOSync.MetadataTests/Events/EventQueryServiceTests.cs`

**Interfaces:**
- Consumes: `EventFilter`, `EventSummaryDto`, `EventDetailDto`, `PagedResult<T>` (Task 1); `AppDbContext` (Persistence)
- Produces:
  - `IEventQueryService.GetEventsAsync(EventFilter filter, CancellationToken ct) → Task<PagedResult<EventSummaryDto>>`
  - `IEventQueryService.GetEventByIdAsync(long eventId, CancellationToken ct) → Task<EventDetailDto?>`

---

- [ ] **Step 1: Write failing tests**

Create `tests/MSOSync.MetadataTests/Events/EventQueryServiceTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Events;

public sealed class EventQueryServiceTests
{
    private static (EventQueryService Svc, AppDbContext Db) Make()
    {
        var db  = TestDbContext.Create();
        var svc = new EventQueryService(db);
        return (svc, db);
    }

    private static SyncDataEvent MakeEvent(string nodeId, string triggerId, char eventType,
        bool isProcessed = false) => new()
    {
        TriggerId    = triggerId,
        SourceNodeId = nodeId,
        ChannelId    = "ch-1",
        EventType    = eventType,
        TableName    = "dbo.Product",
        CreateTime   = DateTime.UtcNow
    };

    [Fact]
    public async Task GetEventsAsync_NoFilter_ReturnsAll()
    {
        var (svc, db) = Make();
        db.DataEvents.AddRange(
            MakeEvent("node-1", "trig-a", 'I'),
            MakeEvent("node-2", "trig-b", 'U'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter(), default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetEventsAsync_FilterBySourceNode_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.DataEvents.AddRange(
            MakeEvent("node-1", "trig-a", 'I'),
            MakeEvent("node-2", "trig-b", 'U'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { SourceNodeId = "node-1" }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().SourceNodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task GetEventsAsync_FilterByEventType_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.DataEvents.AddRange(
            MakeEvent("node-1", "trig-a", 'I'),
            MakeEvent("node-1", "trig-b", 'U'),
            MakeEvent("node-1", "trig-c", 'D'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { EventType = 'U' }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().EventType.Should().Be('U');
    }

    [Fact]
    public async Task GetEventsAsync_FilterByIsProcessed_ReturnsMatching()
    {
        var (svc, db) = Make();
        var e1 = MakeEvent("node-1", "trig-a", 'I');
        var e2 = MakeEvent("node-1", "trig-b", 'U');
        e2.IsProcessed = true;
        db.DataEvents.AddRange(e1, e2);
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { IsProcessed = true }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().IsProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task GetEventsAsync_FilterByDateRange_ReturnsMatching()
    {
        var (svc, db) = Make();
        var old = MakeEvent("node-1", "trig-a", 'I');
        old.CreateTime = DateTime.UtcNow.AddDays(-10);
        var recent = MakeEvent("node-1", "trig-b", 'U');
        recent.CreateTime = DateTime.UtcNow;
        db.DataEvents.AddRange(old, recent);
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(
            new EventFilter { From = DateTime.UtcNow.AddDays(-1) }, default);

        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetEventsAsync_Pagination_HonorsPageSize()
    {
        var (svc, db) = Make();
        for (int i = 0; i < 10; i++)
            db.DataEvents.Add(MakeEvent("node-1", $"trig-{i}", 'I'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter { Page = 1, PageSize = 3 }, default);

        result.TotalCount.Should().Be(10);
        result.Items.Should().HaveCount(3);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(3);
    }

    [Fact]
    public async Task GetEventsAsync_EventWithBatch_ReturnsBatchId()
    {
        var (svc, db) = Make();
        var ev = MakeEvent("node-1", "trig-a", 'I');
        db.DataEvents.Add(ev);
        await db.SaveChangesAsync();

        db.DataEventBatches.Add(new SyncDataEventBatch { EventId = ev.EventId, BatchId = 999L });
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter(), default);

        result.Items.Single().BatchId.Should().Be(999L);
    }

    [Fact]
    public async Task GetEventsAsync_EventWithoutBatch_BatchIdNull()
    {
        var (svc, db) = Make();
        db.DataEvents.Add(MakeEvent("node-1", "trig-a", 'I'));
        await db.SaveChangesAsync();

        var result = await svc.GetEventsAsync(new EventFilter(), default);

        result.Items.Single().BatchId.Should().BeNull();
    }

    [Fact]
    public async Task GetEventByIdAsync_Exists_ReturnsDetailDto()
    {
        var (svc, db) = Make();
        var ev = MakeEvent("node-1", "trig-a", 'I');
        ev.PkData = "{\"id\":1}";
        db.DataEvents.Add(ev);
        await db.SaveChangesAsync();

        var dto = await svc.GetEventByIdAsync(ev.EventId, default);

        dto.Should().NotBeNull();
        dto!.EventId.Should().Be(ev.EventId);
        dto.PkData.Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task GetEventByIdAsync_Missing_ReturnsNull()
    {
        var (svc, db) = Make();
        var dto = await svc.GetEventByIdAsync(99999L, default);
        dto.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build tests\MSOSync.MetadataTests -c Debug
```

Expected: compile error — `EventQueryService`, `IEventQueryService` not found.

- [ ] **Step 3: Create interface**

Create `src/MSOSync.Metadata/Events/IEventQueryService.cs`:

```csharp
using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.Events;

public interface IEventQueryService
{
    Task<PagedResult<EventSummaryDto>> GetEventsAsync(
        EventFilter filter, CancellationToken ct = default);

    Task<EventDetailDto?> GetEventByIdAsync(
        long eventId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement EventQueryService**

Create `src/MSOSync.Metadata/Events/EventQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Events;

public sealed class EventQueryService(AppDbContext db) : IEventQueryService
{
    public async Task<PagedResult<EventSummaryDto>> GetEventsAsync(
        EventFilter filter, CancellationToken ct = default)
    {
        var q = db.DataEvents.AsNoTracking();

        if (filter.SourceNodeId is not null) q = q.Where(e => e.SourceNodeId == filter.SourceNodeId);
        if (filter.TriggerId    is not null) q = q.Where(e => e.TriggerId    == filter.TriggerId);
        if (filter.ChannelId    is not null) q = q.Where(e => e.ChannelId    == filter.ChannelId);
        if (filter.EventType    is not null) q = q.Where(e => e.EventType    == filter.EventType);
        if (filter.IsProcessed  is not null) q = q.Where(e => e.IsProcessed  == filter.IsProcessed);
        if (filter.From         is not null) q = q.Where(e => e.CreateTime   >= filter.From);
        if (filter.To           is not null) q = q.Where(e => e.CreateTime   <= filter.To);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(e => e.CreateTime)
            .Select(e => new EventSummaryDto(
                e.EventId,
                e.TriggerId,
                e.SourceNodeId,
                e.ChannelId,
                e.EventType,
                e.TableName,
                db.DataEventBatches
                    .Where(deb => deb.EventId == e.EventId)
                    .Max(deb => (long?)deb.BatchId),
                e.CreateTime,
                e.IsProcessed))
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new PagedResult<EventSummaryDto>(items.AsReadOnly(), filter.Page, filter.PageSize, total);
    }

    public async Task<EventDetailDto?> GetEventByIdAsync(
        long eventId, CancellationToken ct = default)
    {
        var e = await db.DataEvents.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .FirstOrDefaultAsync(ct);

        if (e is null) return null;

        var batchId = await db.DataEventBatches
            .AsNoTracking()
            .Where(deb => deb.EventId == eventId)
            .MaxAsync(deb => (long?)deb.BatchId, ct);

        return new EventDetailDto(
            e.EventId, e.TriggerId, e.SourceNodeId, e.ChannelId,
            e.EventType, e.TableName, e.PkData, e.RowData, e.TransactionId,
            batchId, e.CreateTime, e.IsProcessed);
    }
}
```

- [ ] **Step 5: Run tests**

```powershell
dotnet test tests\MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~EventQueryService" --logger "console;verbosity=normal"
```

Expected: all 10 tests PASS.

- [ ] **Step 6: Verify build**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Metadata/Events/IEventQueryService.cs
git add src/MSOSync.Metadata/Events/EventQueryService.cs
git add tests/MSOSync.MetadataTests/Events/EventQueryServiceTests.cs
git commit -m "feat(9a): add IEventQueryService + EventQueryService with SQLite unit tests"
```
