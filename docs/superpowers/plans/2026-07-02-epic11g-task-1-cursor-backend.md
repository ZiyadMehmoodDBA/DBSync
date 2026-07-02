# Task 1: Cursor Pagination — Backend

**Part of:** Epic 11G — Performance & Scale  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11g-performance-scale-design.md`  
**Depends on:** nothing (first task)

## Files

**Create:**
- `src/MSOSync.Common/Pagination/CursorPageResult.cs`
- `src/MSOSync.Common/Pagination/CursorToken.cs`
- `tests/MSOSync.MetadataTests/Pagination/CursorTokenTests.cs`

**Modify (Events):**
- `src/MSOSync.Metadata/Events/EventFilter.cs` — remove `Page`, add `Cursor?` + `IncludeTotalCount`
- `src/MSOSync.Metadata/Events/EventFilterValidator.cs` — remove Page rule
- `src/MSOSync.Metadata/Events/IEventQueryService.cs` — return type → `CursorPageResult<T>`
- `src/MSOSync.Metadata/Events/EventQueryService.cs` — cursor query logic

**Modify (IncomingBatches, OutgoingBatches, Audit — same pattern):**
- `src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs`
- `src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilterValidator.cs`
- `src/MSOSync.Metadata/IncomingBatches/IIncomingBatchQueryService.cs`
- `src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs`
- `src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchFilter.cs`
- `src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchFilterValidator.cs`
- `src/MSOSync.Metadata/OutgoingBatches/IOutgoingBatchQueryService.cs`
- `src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchQueryService.cs`
- `src/MSOSync.Metadata/Audit/AuditFilter.cs`
- `src/MSOSync.Metadata/Audit/AuditFilterValidator.cs`
- `src/MSOSync.Metadata/Audit/IAuditQueryService.cs`
- `src/MSOSync.Metadata/Audit/AuditQueryService.cs`

**Modify (Nodes — offset pagination, first-time):**
- `src/MSOSync.Metadata/Nodes/INodeQueryService.cs`
- `src/MSOSync.Metadata/Nodes/NodeQueryService.cs`

**Modify (Controllers):**
- `src/MSOSync.Api/Controllers/EventsController.cs`
- `src/MSOSync.Api/Controllers/IncomingBatchesController.cs`
- `src/MSOSync.Api/Controllers/OutgoingBatchesController.cs`
- `src/MSOSync.Api/Controllers/AuditController.cs`
- `src/MSOSync.Api/Controllers/NodesController.cs`

## Interfaces Produced (consumed by Task 2)

```csharp
// MSOSync.Common.Pagination
CursorPageResult<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore, int? TotalCount)
CursorToken.Encode(long id, long ticks) → string
CursorToken.Decode(string token) → (long Id, long Ticks)   // throws ArgumentException on bad input

// Updated filter shape (Events example — same pattern for all 4):
EventFilter.Cursor        string?   // null = start from beginning
EventFilter.IncludeTotalCount bool  // default false
// EventFilter.Page REMOVED

// Updated service interfaces:
IEventQueryService.GetEventsAsync(EventFilter, CancellationToken) → Task<CursorPageResult<EventSummaryDto>>
IIncomingBatchQueryService.GetIncomingBatchesAsync(IncomingBatchFilter, ct) → Task<CursorPageResult<IncomingBatchSummaryDto>>
IOutgoingBatchQueryService.GetOutgoingBatchesAsync(OutgoingBatchFilter, ct) → Task<CursorPageResult<OutgoingBatchSummaryDto>>
IAuditQueryService.GetAuditLogAsync(AuditFilter, ct) → Task<CursorPageResult<AuditEntryDto>>

// Nodes (offset, not cursor):
INodeQueryService.GetNodesPagedAsync(int pageNumber, int pageSize, CancellationToken) → Task<PagedResult<NodeSummaryDto>>
```

---

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- `AsNoTracking()` on all reads
- No new NuGet packages
- Build env: `$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"` and `$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"`

---

- [ ] **Step 1: Read existing event infrastructure**

Before writing anything, read these files to understand exact field names, types, and validators:

```pwsh
# Read the files you will modify
```

Files to read:
- `src/MSOSync.Metadata/Events/EventFilter.cs` — exact property names
- `src/MSOSync.Metadata/Events/EventFilterValidator.cs` — existing validator rules
- `src/MSOSync.Metadata/Events/IEventQueryService.cs` — current interface
- `src/MSOSync.Api/Controllers/EventsController.cs` — already read in context
- `src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs`
- `src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs`
- `src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchFilter.cs`
- `src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchQueryService.cs`
- `src/MSOSync.Metadata/Audit/AuditFilter.cs`
- `src/MSOSync.Metadata/Audit/AuditQueryService.cs`
- `src/MSOSync.Metadata/Nodes/NodeQueryService.cs`
- `src/MSOSync.Api/Controllers/NodesController.cs`

Note the exact PK field names on each entity (you'll see them in the query services). You need:
- `SyncDataEvent` PK → likely `EventId` (long)
- `SyncIncomingBatch` PK → find in service's `.OrderByDescending(...)` or `.Where(b => b.BatchId ...)`
- `SyncOutgoingBatch` PK
- `SyncAudit` PK

- [ ] **Step 2: Create `CursorPageResult<T>`**

```csharp
// src/MSOSync.Common/Pagination/CursorPageResult.cs
namespace MSOSync.Common.Pagination;

public sealed record CursorPageResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore,
    int? TotalCount
);
```

- [ ] **Step 3: Create `CursorToken`**

```csharp
// src/MSOSync.Common/Pagination/CursorToken.cs
using System.Text;

namespace MSOSync.Common.Pagination;

public static class CursorToken
{
    public static string Encode(long id, long ticks)
    {
        var raw = $"v1:{id}:{ticks}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    public static (long Id, long Ticks) Decode(string token)
    {
        string raw;
        try { raw = Encoding.UTF8.GetString(Convert.FromBase64String(token)); }
        catch { throw new ArgumentException("Invalid cursor token."); }

        var parts = raw.Split(':');
        if (parts.Length != 3 || parts[0] != "v1")
            throw new ArgumentException("Invalid cursor token format.");

        if (!long.TryParse(parts[1], out var id) || !long.TryParse(parts[2], out var ticks))
            throw new ArgumentException("Invalid cursor token values.");

        return (id, ticks);
    }
}
```

- [ ] **Step 4: Write CursorToken unit tests**

```csharp
// tests/MSOSync.MetadataTests/Pagination/CursorTokenTests.cs
using MSOSync.Common.Pagination;
using FluentAssertions;
using Xunit;

namespace MSOSync.MetadataTests.Pagination;

public sealed class CursorTokenTests
{
    [Fact]
    public void Encode_ThenDecode_ReturnsOriginalValues()
    {
        var token = CursorToken.Encode(12345L, 637800000000000000L);
        var (id, ticks) = CursorToken.Decode(token);
        id.Should().Be(12345L);
        ticks.Should().Be(637800000000000000L);
    }

    [Fact]
    public void Encode_ProducesOpaqueBase64()
    {
        var token = CursorToken.Encode(1L, 0L);
        Convert.FromBase64String(token).Should().NotBeEmpty(); // valid base64
        token.Should().NotContain("1");  // opaque — raw id not visible
    }

    [Fact]
    public void Decode_GarbageInput_ThrowsArgumentException()
    {
        var act = () => CursorToken.Decode("not-valid-base64!!!");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_WrongVersion_ThrowsArgumentException()
    {
        var raw = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("v2:1:0"));
        var act = () => CursorToken.Decode(raw);
        act.Should().Throw<ArgumentException>().WithMessage("*format*");
    }

    [Fact]
    public void Decode_NonNumericId_ThrowsArgumentException()
    {
        var raw = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("v1:abc:0"));
        var act = () => CursorToken.Decode(raw);
        act.Should().Throw<ArgumentException>().WithMessage("*values*");
    }

    [Fact]
    public void Encode_ZeroValues_RoundTrips()
    {
        var token = CursorToken.Encode(0L, 0L);
        var (id, ticks) = CursorToken.Decode(token);
        id.Should().Be(0L);
        ticks.Should().Be(0L);
    }

    [Fact]
    public void Encode_MaxLong_RoundTrips()
    {
        var token = CursorToken.Encode(long.MaxValue, long.MaxValue);
        var (id, ticks) = CursorToken.Decode(token);
        id.Should().Be(long.MaxValue);
        ticks.Should().Be(long.MaxValue);
    }
}
```

- [ ] **Step 5: Run CursorToken tests — expect all pass**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~CursorTokenTests" -c Debug 2>&1 | Select-Object -Last 8
```

Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 6: Update `EventFilter` — remove `Page`, add `Cursor` + `IncludeTotalCount`**

Open `src/MSOSync.Metadata/Events/EventFilter.cs`. Remove the `Page` property. Add:

```csharp
public string? Cursor { get; init; }
public bool IncludeTotalCount { get; init; }
```

Keep `PageSize` (change its max in the validator). After the edit, `EventFilter` should have `Cursor?`, `PageSize`, `IncludeTotalCount`, and all the domain filter fields (SourceNodeId, TriggerId, etc.) — but no `Page`.

- [ ] **Step 7: Update `EventFilterValidator` — remove Page rule, adjust PageSize max**

In `src/MSOSync.Metadata/Events/EventFilterValidator.cs`, remove the `RuleFor(x => x.Page)` rule. Update `PageSize` validation to allow up to 500 (operators loading more data):

```csharp
RuleFor(x => x.PageSize).InclusiveBetween(1, 500);
```

- [ ] **Step 8: Update `IEventQueryService` return type**

In `src/MSOSync.Metadata/Events/IEventQueryService.cs`, change the signature:

```csharp
using MSOSync.Common.Pagination;

// Before:
Task<PagedResult<EventSummaryDto>> GetEventsAsync(EventFilter filter, CancellationToken ct = default);

// After:
Task<CursorPageResult<EventSummaryDto>> GetEventsAsync(EventFilter filter, CancellationToken ct = default);
```

- [ ] **Step 9: Rewrite `EventQueryService.GetEventsAsync` with cursor logic**

Replace the existing `GetEventsAsync` method body. The key changes: no `CountAsync` by default, no `Skip`, cursor-based `WHERE Id < @cursor`, fetch `pageSize + 1` to determine `HasMore`.

```csharp
using MSOSync.Common.Pagination;

public async Task<CursorPageResult<EventSummaryDto>> GetEventsAsync(
    EventFilter filter, CancellationToken ct = default)
{
    var baseQ = db.DataEvents.AsNoTracking();

    if (filter.SourceNodeId is not null) baseQ = baseQ.Where(e => e.SourceNodeId == filter.SourceNodeId);
    if (filter.TriggerId    is not null) baseQ = baseQ.Where(e => e.TriggerId    == filter.TriggerId);
    if (filter.ChannelId    is not null) baseQ = baseQ.Where(e => e.ChannelId    == filter.ChannelId);
    if (filter.EventType    is not null) baseQ = baseQ.Where(e => e.EventType    == filter.EventType);
    if (filter.IsProcessed  is not null) baseQ = baseQ.Where(e => e.IsProcessed  == filter.IsProcessed);
    if (filter.From         is not null) baseQ = baseQ.Where(e => e.CreateTime   >= filter.From);
    if (filter.To           is not null) baseQ = baseQ.Where(e => e.CreateTime   <= filter.To);

    // Apply cursor — comes AFTER base filters so totalCount counts all matching rows
    var q = baseQ;
    if (filter.Cursor is not null)
    {
        var (cursorId, _) = CursorToken.Decode(filter.Cursor);
        q = q.Where(e => e.EventId < cursorId);
    }

    var pageSize = filter.PageSize;
    var rows = await q
        .OrderByDescending(e => e.EventId)
        .Take(pageSize + 1)
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
        .ToListAsync(ct);

    var hasMore = rows.Count > pageSize;
    if (hasMore) rows = rows.Take(pageSize).ToList();

    string? nextCursor = null;
    if (hasMore)
    {
        var last = rows[^1];
        nextCursor = CursorToken.Encode(last.EventId, last.CreateTime.Ticks);
    }

    int? totalCount = null;
    if (filter.IncludeTotalCount)
        totalCount = await baseQ.CountAsync(ct);

    return new CursorPageResult<EventSummaryDto>(rows.AsReadOnly(), nextCursor, hasMore, totalCount);
}
```

Note: `EventSummaryDto` must expose `.EventId` and `.CreateTime`. Verify the record's positional properties match. If the DTO uses different names, adjust accordingly.

- [ ] **Step 10: Apply the same cursor pattern to `IncomingBatchQueryService`, `OutgoingBatchQueryService`, `AuditQueryService`**

After reading those files (Step 1), apply the same changes:

For each service:
1. Add `string? Cursor` + `bool IncludeTotalCount` to its filter class
2. Remove `Page` from the filter class and its validator
3. Update the interface return type to `CursorPageResult<T>`
4. Rewrite `GetXxxAsync` using the same pattern as Step 9:
   - `var baseQ = ...` (base filters applied)
   - `var q = baseQ` then apply cursor if present (`WHERE PkId < cursorId`)
   - `OrderByDescending(x => x.PkId).Take(pageSize + 1)`
   - `hasMore` check, trim list, build `nextCursor` from last item's PK + CreateTime (or equivalent timestamp) ticks
   - Optional `CountAsync` on `baseQ` if `IncludeTotalCount`

Use the entity's **integer primary key** for cursor ordering (not a timestamp). Find the PK field name from reading the service file in Step 1.

For `AuditQueryService`, the PK is likely `AuditId` on `SyncAudit`. Verify.

- [ ] **Step 11: Add offset pagination to `NodeQueryService`**

Read `src/MSOSync.Metadata/Nodes/NodeQueryService.cs`. Add a new method (do NOT modify existing `GetNodesAsync` — keep it if it's used elsewhere; add a new paged version):

In `INodeQueryService`:
```csharp
Task<PagedResult<NodeSummaryDto>> GetNodesPagedAsync(
    int pageNumber, int pageSize, CancellationToken ct = default);
```

In `NodeQueryService` (use the `PagedResult<T>` from `MSOSync.Metadata.Common`, which already exists):
```csharp
public async Task<PagedResult<NodeSummaryDto>> GetNodesPagedAsync(
    int pageNumber, int pageSize, CancellationToken ct = default)
{
    var q = db.Nodes.AsNoTracking();
    var total = await q.CountAsync(ct);
    var items = await q
        .OrderBy(n => n.NodeId)   // stable order — adjust field name to match actual entity
        .Skip((pageNumber - 1) * pageSize)
        .Take(pageSize)
        .Select(n => new NodeSummaryDto(/* match existing DTO constructor */))
        .ToListAsync(ct);
    return new PagedResult<NodeSummaryDto>(items.AsReadOnly(), pageNumber, pageSize, total);
}
```

Adjust `NodeId` and `NodeSummaryDto` constructor to match the actual entity and DTO fields you see in the existing service.

- [ ] **Step 12: Update controllers**

For `EventsController.GetEvents`: the filter binding from query string already works — no param changes needed in the controller since `EventFilter` is `[FromQuery]`. The return type changes automatically. The controller method stays the same. Build will verify.

For `NodesController`: read it, then add a paged endpoint (or update the existing one to accept `pageNumber` + `pageSize`):

```csharp
[HttpGet]
[ProducesResponseType(typeof(PagedResult<NodeSummaryDto>), 200)]
public async Task<IActionResult> GetNodes(
    [FromQuery] int pageNumber = 1,
    [FromQuery] int pageSize   = 50,
    CancellationToken ct = default)
{
    pageSize = Math.Min(pageSize, 200);  // hard cap
    return Ok(await nodeService.GetNodesPagedAsync(pageNumber, pageSize, ct));
}
```

For the other three stream controllers (IncomingBatches, OutgoingBatches, Audit): same as EventsController — no controller changes needed since the filter handles it all. Verify by building.

- [ ] **Step 13: Build `MSOSync.Metadata` and `MSOSync.Api` — expect zero warnings**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.Metadata -c Debug --warnaserror 2>&1 | Select-Object -Last 5
dotnet build src/MSOSync.Api -c Debug --warnaserror 2>&1 | Select-Object -Last 5
```

Expected: `Build succeeded. 0 Warning(s)` for both.

If there are compile errors: the most likely cause is a reference to `PagedResult<T>` that now needs to be `CursorPageResult<T>`, or a missing `using MSOSync.Common.Pagination` directive. Fix all errors before proceeding.

- [ ] **Step 14: Run all MetadataTests to confirm nothing broken**

```pwsh
dotnet test tests/MSOSync.MetadataTests -c Debug 2>&1 | Select-Object -Last 8
```

Expected: all tests pass. If existing tests reference `PagedResult` from event/batch/audit services, they need to be updated to use `CursorPageResult`. Fix any failing tests to match the new return type.

- [ ] **Step 15: Commit**

```pwsh
git add `
  src/MSOSync.Common/Pagination/CursorPageResult.cs `
  src/MSOSync.Common/Pagination/CursorToken.cs `
  src/MSOSync.Metadata/Events/EventFilter.cs `
  src/MSOSync.Metadata/Events/EventFilterValidator.cs `
  src/MSOSync.Metadata/Events/IEventQueryService.cs `
  src/MSOSync.Metadata/Events/EventQueryService.cs `
  src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs `
  src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilterValidator.cs `
  src/MSOSync.Metadata/IncomingBatches/IIncomingBatchQueryService.cs `
  src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs `
  src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchFilter.cs `
  src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchFilterValidator.cs `
  src/MSOSync.Metadata/OutgoingBatches/IOutgoingBatchQueryService.cs `
  src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchQueryService.cs `
  src/MSOSync.Metadata/Audit/AuditFilter.cs `
  src/MSOSync.Metadata/Audit/AuditFilterValidator.cs `
  src/MSOSync.Metadata/Audit/IAuditQueryService.cs `
  src/MSOSync.Metadata/Audit/AuditQueryService.cs `
  src/MSOSync.Metadata/Nodes/INodeQueryService.cs `
  src/MSOSync.Metadata/Nodes/NodeQueryService.cs `
  src/MSOSync.Api/Controllers/EventsController.cs `
  src/MSOSync.Api/Controllers/IncomingBatchesController.cs `
  src/MSOSync.Api/Controllers/OutgoingBatchesController.cs `
  src/MSOSync.Api/Controllers/AuditController.cs `
  src/MSOSync.Api/Controllers/NodesController.cs `
  tests/MSOSync.MetadataTests/Pagination/CursorTokenTests.cs

git commit -m "feat(11g): add CursorToken + CursorPageResult + cursor pagination on 4 stream endpoints + Nodes bounded"
```

## Status Report Format

```
Status: DONE
Commit: <sha>
Tests: <N> passed, 0 failed
Concerns: <none or list>
```
