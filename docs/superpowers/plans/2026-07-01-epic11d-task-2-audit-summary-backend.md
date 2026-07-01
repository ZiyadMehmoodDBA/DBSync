# Task 2: Audit Summary Backend

**Part of:** Epic 11D — Export + Audit Intelligence  
**Spec:** `docs/superpowers/specs/2026-07-01-epic11d-export-audit-intelligence-design.md`  
**Depends on:** Task 1 (AuditController changes are additive; export action already added)

## Files

**Create:**
- `src/MSOSync.Metadata/Audit/AuditSummaryDto.cs`
- `src/MSOSync.Metadata/Audit/IAuditSummaryService.cs`
- `src/MSOSync.Metadata/Audit/AuditSummaryService.cs`
- `tests/MSOSync.MetadataTests/Audit/AuditSummaryServiceTests.cs`

**Modify:**
- `src/MSOSync.Api/Controllers/AuditController.cs` (add `GET /api/v1/audit/summary`)
- `src/MSOSync.Metadata/MetadataServiceExtensions.cs` (register `IAuditSummaryService`)

## Interfaces Produced (consumed by Task 4 frontend)

```csharp
// IAuditSummaryService
Task<AuditSummaryDto> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct);

// GET /api/v1/audit/summary?from=ISO8601&to=ISO8601 → AuditSummaryDto JSON
```

---

## Global Constraints (apply to every step)

- C# 13, .NET 9, `TreatWarningsAsErrors = true`
- EF Core 9 — all queries `AsNoTracking()`; grouped queries are separate `CountAsync` and `GroupBy` calls
- Action names in `sync_audit` use `SCREAMING_SNAKE_CASE` (e.g. `LOGIN_FAILURE`, `ACCOUNT_LOCKED`)
- `ByDay` must include zero-count days for every calendar day between `from.Date` and `to.Date` (inclusive)
- Unit tests: `TestDbContext.Create()` (SQLite in-memory)

---

- [ ] **Step 1: Create AuditSummaryDto.cs**

```csharp
// src/MSOSync.Metadata/Audit/AuditSummaryDto.cs
namespace MSOSync.Metadata.Audit;

public sealed record AuditSummaryDto(
    int                             TotalActions,
    int                             FailedOperations,
    int                             PermissionChanges,
    IReadOnlyList<DayBucket>        ByDay,
    IReadOnlyList<UserBucket>       ByUser,
    IReadOnlyList<EntityTypeBucket> ByEntityType,
    IReadOnlyList<ParameterBucket>  TopParameters
);

public sealed record DayBucket(DateOnly Date, int Total, int Failed);
public sealed record UserBucket(string Username, int Count);
public sealed record EntityTypeBucket(string EntityType, int Count);
public sealed record ParameterBucket(string ParameterName, int Count);
```

- [ ] **Step 2: Create IAuditSummaryService.cs**

```csharp
// src/MSOSync.Metadata/Audit/IAuditSummaryService.cs
namespace MSOSync.Metadata.Audit;

public interface IAuditSummaryService
{
    Task<AuditSummaryDto> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write failing tests for AuditSummaryService**

```csharp
// tests/MSOSync.MetadataTests/Audit/AuditSummaryServiceTests.cs
using FluentAssertions;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Audit;

public sealed class AuditSummaryServiceTests : IDisposable
{
    private readonly AppDbContext      _db;
    private readonly AuditSummaryService _sut;
    private static readonly DateTime BaseTime = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    public AuditSummaryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new AuditSummaryService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncAudit Audit(long id, string actionName = "LOGIN_SUCCESS",
        string? username = "alice", string? objectName = null,
        DateTime? createTime = null) => new()
    {
        AuditId    = id,
        ActionName = actionName,
        Username   = username,
        ObjectName = objectName,
        CreateTime = createTime ?? BaseTime,
    };

    [Fact]
    public async Task GetSummary_CountsTotalActions()
    {
        _db.Audits.AddRange(Audit(1), Audit(2), Audit(3));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.TotalActions.Should().Be(3);
    }

    [Fact]
    public async Task GetSummary_CountsFailedOperations()
    {
        _db.Audits.AddRange(
            Audit(1, actionName: "LOGIN_SUCCESS"),
            Audit(2, actionName: "LOGIN_FAILURE"),
            Audit(3, actionName: "ACCOUNT_LOCKED"),
            Audit(4, actionName: "TOKEN_REUSE_DETECTED"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.FailedOperations.Should().Be(3); // FAILURE, LOCKED, REUSE
    }

    [Fact]
    public async Task GetSummary_CountsPermissionChanges()
    {
        _db.Audits.AddRange(
            Audit(1, actionName: "PERMISSION_GRANTED"),
            Audit(2, actionName: "ROLE_UPDATED"),
            Audit(3, actionName: "LOGIN_SUCCESS"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.PermissionChanges.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_ByDay_IncludesZeroCountDays()
    {
        // Only one audit on day 1; day 2 has no audits
        var day1 = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
        _db.Audits.Add(Audit(1, createTime: day1));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(day1.AddHours(-1), day2.AddHours(1));

        result.ByDay.Should().HaveCount(2);
        result.ByDay.First(d => d.Date == DateOnly.FromDateTime(day1)).Total.Should().Be(1);
        result.ByDay.First(d => d.Date == DateOnly.FromDateTime(day2)).Total.Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_ByDay_TracksDailyFailures()
    {
        _db.Audits.AddRange(
            Audit(1, actionName: "LOGIN_SUCCESS", createTime: BaseTime),
            Audit(2, actionName: "LOGIN_FAILURE", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        var bucket = result.ByDay.Single(d => d.Date == DateOnly.FromDateTime(BaseTime));
        bucket.Total.Should().Be(2);
        bucket.Failed.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_ByUser_TopTenDescending()
    {
        for (int i = 1; i <= 12; i++)
            _db.Audits.Add(Audit(i, username: $"user{i:D2}", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.ByUser.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetSummary_ByEntityType_GroupsByObjectName()
    {
        _db.Audits.AddRange(
            Audit(1, objectName: "SyncNode",    createTime: BaseTime),
            Audit(2, objectName: "SyncNode",    createTime: BaseTime),
            Audit(3, objectName: "SyncTrigger", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.ByEntityType.Should().HaveCount(2);
        result.ByEntityType.First().Count.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_ExcludesAuditsOutsideDateRange()
    {
        _db.Audits.AddRange(
            Audit(1, createTime: BaseTime.AddDays(-2)),   // before range
            Audit(2, createTime: BaseTime),                // in range
            Audit(3, createTime: BaseTime.AddDays(2)));   // after range
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(
            BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.TotalActions.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_TopParameters_LimitsTenByParameterAction()
    {
        for (int i = 1; i <= 15; i++)
            _db.Audits.Add(Audit(i, actionName: "PARAMETER_UPDATED",
                objectName: $"param{i:D2}", createTime: BaseTime));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(BaseTime.AddHours(-1), BaseTime.AddHours(1));

        result.TopParameters.Should().HaveCount(10);
    }
}
```

- [ ] **Step 4: Run tests — verify they fail with "AuditSummaryService not found"**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~AuditSummaryServiceTests" 2>&1 | Select-Object -Last 5
```

Expected: build error.

- [ ] **Step 5: Create AuditSummaryService**

```csharp
// src/MSOSync.Metadata/Audit/AuditSummaryService.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Audit;

public sealed class AuditSummaryService(AppDbContext db) : IAuditSummaryService
{
    public async Task<AuditSummaryDto> GetSummaryAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var base_q = db.Audits.AsNoTracking()
            .Where(a => a.CreateTime != null
                     && a.CreateTime >= from
                     && a.CreateTime <= to);

        var totalActions = await base_q.CountAsync(ct);

        var failedOperations = await base_q
            .CountAsync(a => a.ActionName != null && (
                a.ActionName.Contains("FAILURE") ||
                a.ActionName.Contains("FAILED")  ||
                a.ActionName.Contains("ERROR")   ||
                a.ActionName.Contains("LOCKED")  ||
                a.ActionName.Contains("REUSE")), ct);

        var permissionChanges = await base_q
            .CountAsync(a => a.ActionName != null && (
                a.ActionName.Contains("PERMISSION") ||
                a.ActionName.Contains("ROLE")       ||
                a.ActionName.Contains("GRANT")      ||
                a.ActionName.Contains("REVOKE")), ct);

        // ByDay — group then zero-fill
        var byDayRaw = await base_q
            .GroupBy(a => a.CreateTime!.Value.Date)
            .Select(g => new
            {
                Date  = g.Key,
                Total = g.Count(),
                Failed = g.Count(a => a.ActionName != null && (
                    a.ActionName.Contains("FAILURE") ||
                    a.ActionName.Contains("FAILED")  ||
                    a.ActionName.Contains("ERROR")   ||
                    a.ActionName.Contains("LOCKED")  ||
                    a.ActionName.Contains("REUSE")))
            })
            .ToDictionaryAsync(x => x.Date, x => (x.Total, x.Failed), ct);

        var totalDays = (int)(to.Date - from.Date).TotalDays + 1;
        var byDay = Enumerable.Range(0, totalDays)
            .Select(i => from.Date.AddDays(i))
            .Select(d => byDayRaw.TryGetValue(d, out var b)
                ? new DayBucket(DateOnly.FromDateTime(d), b.Total, b.Failed)
                : new DayBucket(DateOnly.FromDateTime(d), 0, 0))
            .ToList();

        // ByUser — top 10
        var byUser = await base_q
            .Where(a => a.Username != null)
            .GroupBy(a => a.Username!)
            .Select(g => new UserBucket(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        // ByEntityType — group by ObjectName
        var byEntityType = await base_q
            .Where(a => a.ObjectName != null)
            .GroupBy(a => a.ObjectName!)
            .Select(g => new EntityTypeBucket(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        // TopParameters — parameter-related actions, top 10 by parameter name
        var topParameters = await base_q
            .Where(a => a.ActionName != null && a.ActionName.Contains("PARAMETER")
                     && a.ObjectName != null)
            .GroupBy(a => a.ObjectName!)
            .Select(g => new ParameterBucket(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        return new AuditSummaryDto(
            totalActions,
            failedOperations,
            permissionChanges,
            byDay.AsReadOnly(),
            byUser.AsReadOnly(),
            byEntityType.AsReadOnly(),
            topParameters.AsReadOnly());
    }
}
```

- [ ] **Step 6: Run AuditSummaryService tests — all must pass**

```pwsh
dotnet test tests/MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~AuditSummaryServiceTests" 2>&1 | Select-Object -Last 15
```

Expected: 8 tests pass, 0 failed.

- [ ] **Step 7: Add summary action to AuditController**

Add `IAuditSummaryService summaryService` to `AuditController`'s primary constructor.

Then add this action to the class:

```csharp
// In AuditController — add after GetAuditById
[HttpGet("summary")]
[ProducesResponseType(200)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
public async Task<IActionResult> GetAuditSummary(
    [FromQuery] DateTime from,
    [FromQuery] DateTime to,
    CancellationToken ct)
{
    if (from >= to)
        return BadRequest(new { code = "INVALID_RANGE", message = "'from' must be before 'to'" });
    return Ok(await summaryService.GetSummaryAsync(from, to, ct));
}
```

Add the using at the top of AuditController if not already present:
```csharp
using MSOSync.Metadata.Audit;
```

- [ ] **Step 8: Register IAuditSummaryService in MetadataServiceExtensions**

In `MetadataServiceExtensions.cs`, inside `AddMetadata`, add after the Epic 11D export block:

```csharp
// Epic 11D — Audit summary
services.AddScoped<IAuditSummaryService, AuditSummaryService>();
```

- [ ] **Step 9: Build and run all tests**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror 2>&1 | Select-Object -Last 5
dotnet test tests/MSOSync.MetadataTests -c Debug 2>&1 | Select-Object -Last 10
```

Expected: build clean, all tests pass.

- [ ] **Step 10: Commit**

```pwsh
git add `
  src/MSOSync.Metadata/Audit/AuditSummaryDto.cs `
  src/MSOSync.Metadata/Audit/IAuditSummaryService.cs `
  src/MSOSync.Metadata/Audit/AuditSummaryService.cs `
  src/MSOSync.Api/Controllers/AuditController.cs `
  src/MSOSync.Metadata/MetadataServiceExtensions.cs `
  tests/MSOSync.MetadataTests/Audit/AuditSummaryServiceTests.cs

git commit -m "feat: add audit summary service and GET /api/v1/audit/summary endpoint

Grouped EF Core queries return totals, byDay (zero-filled), byUser, byEntityType,
and topParameters. Date range accepted as absolute ISO 8601 from/to params."
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Tests: <N> passed, 0 failed
Concerns: <none or list>
```
