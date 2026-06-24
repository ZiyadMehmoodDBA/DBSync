# Task 5: BatchErrorQueryService

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Implement `IBatchErrorQueryService`. `Severity` is derived from `ConflictType` via `IErrorSeverityClassifier` in memory after SQL projection (not in LINQ — not SQL-translatable). Severity filter translates to `WHERE conflict_type IN (...)` via `GetConflictTypes()`. `GetBatchErrorSummaryAsync` supports optional `batchId`, `from`, and `to` filters.

**Note on `CreateTime`:** `SyncBatchError` does NOT have `CreateTime` yet — that is added in Task 7 (M015 migration). For now, the interface and service use `CreateTime`. The SQLite unit tests will compile and run correctly once M015 adds the property in Task 7. **Write these tests now but skip running them until after Task 7.**

Actually — re-check: if `SyncBatchError.CreateTime` doesn't exist yet, this task's code won't compile. **Approach:** add `CreateTime` to the entity and config in this task (as a forward step), then Task 7 will write the actual migration SQL. This keeps tasks unblocked.

**Revised approach:** Add `CreateTime` to `SyncBatchError` entity in this task so query service compiles. Task 7 writes the migration SQL that adds the column to SQL Server.

**Files:**
- Create: `src/MSOSync.Metadata/BatchErrors/IBatchErrorQueryService.cs`
- Create: `src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs`
- Create: `tests/MSOSync.MetadataTests/BatchErrors/BatchErrorQueryServiceTests.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncBatchError.cs` — add `CreateTime` property
- Modify: `src/MSOSync.Persistence/Configurations/SyncBatchErrorConfiguration.cs` — add `CreateTime` config

**Interfaces:**
- Consumes: `BatchErrorFilter`, `ErrorSeverity`, `BatchErrorSummaryDto`, `BatchErrorDetailDto`, `BatchErrorSummaryCountDto`, `PagedResult<T>` (Task 1); `IErrorSeverityClassifier` (Task 2); `AppDbContext` (Persistence)
- Produces:
  - `IBatchErrorQueryService.GetBatchErrorsAsync(BatchErrorFilter filter, CancellationToken ct) → Task<PagedResult<BatchErrorSummaryDto>>`
  - `IBatchErrorQueryService.GetBatchErrorByIdAsync(long errorId, CancellationToken ct) → Task<BatchErrorDetailDto?>`
  - `IBatchErrorQueryService.GetBatchErrorSummaryAsync(long? batchId, DateTime? from, DateTime? to, CancellationToken ct) → Task<BatchErrorSummaryCountDto>`

---

- [ ] **Step 1: Add CreateTime to SyncBatchError entity**

Open `src/MSOSync.Persistence/Entities/SyncBatchError.cs`. Current content:

```csharp
namespace MSOSync.Persistence.Entities;

public sealed class SyncBatchError
{
    public long ErrorId { get; set; }
    public long BatchId { get; set; }
    public long? EventId { get; set; }
    public string? ConflictType { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; } = 0;
    public DateTime? LastRetryTime { get; set; }
}
```

Add `CreateTime`:

```csharp
namespace MSOSync.Persistence.Entities;

public sealed class SyncBatchError
{
    public long      ErrorId       { get; set; }
    public long      BatchId       { get; set; }
    public long?     EventId       { get; set; }
    public string?   ConflictType  { get; set; }
    public string?   ErrorMessage  { get; set; }
    public int       RetryCount    { get; set; } = 0;
    public DateTime? LastRetryTime { get; set; }
    public DateTime  CreateTime    { get; set; }
}
```

- [ ] **Step 2: Add CreateTime to SyncBatchErrorConfiguration**

Open `src/MSOSync.Persistence/Configurations/SyncBatchErrorConfiguration.cs`.

Add these lines after the `LastRetryTime` property configuration:

```csharp
builder.Property(e => e.CreateTime)
    .HasColumnName("create_time")
    .HasColumnType("datetime2(7)")
    .HasDefaultValueSql("SYSUTCDATETIME()");
```

The full `Configure` method body should end with:

```csharp
builder.Property(e => e.LastRetryTime).HasColumnName("last_retry_time").HasColumnType("datetime2(7)");
builder.Property(e => e.CreateTime)
    .HasColumnName("create_time")
    .HasColumnType("datetime2(7)")
    .HasDefaultValueSql("SYSUTCDATETIME()");

builder.HasIndex(e => e.BatchId).HasDatabaseName("IX_sync_batch_error_batch_id");

builder.HasOne<SyncOutgoingBatch>()
    .WithMany()
    .HasForeignKey(e => e.BatchId)
    .HasConstraintName("FK_sync_batch_error_batch_id")
    .OnDelete(DeleteBehavior.Restrict);
```

Also add the `conflict_create` index (used by BatchErrorQueryService):

```csharp
builder.HasIndex(e => new { e.ConflictType, e.CreateTime })
    .IsDescending(false, true)
    .HasDatabaseName("IX_sync_batch_error_conflict_create");
```

- [ ] **Step 3: Write failing tests**

Create `tests/MSOSync.MetadataTests/BatchErrors/BatchErrorQueryServiceTests.cs`:

```csharp
using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.BatchErrors;

public sealed class BatchErrorQueryServiceTests
{
    private static (BatchErrorQueryService Svc, AppDbContext Db) Make()
    {
        var db         = TestDbContext.Create();
        var classifier = new ErrorSeverityClassifier();
        var svc        = new BatchErrorQueryService(db, classifier);
        return (svc, db);
    }

    private static SyncBatchError MakeError(long batchId, string? conflictType,
        long? eventId = null) => new()
    {
        BatchId      = batchId,
        EventId      = eventId,
        ConflictType = conflictType,
        ErrorMessage = "test error",
        RetryCount   = 0,
        CreateTime   = DateTime.UtcNow
    };

    [Fact]
    public async Task GetBatchErrorsAsync_NoFilter_ReturnsAll()
    {
        var (svc, db) = Make();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),
            MakeError(1L, "Timeout"),
            MakeError(2L, "MetadataMissing"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter(), default);

        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetBatchErrorsAsync_FilterByBatchId_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),
            MakeError(2L, "Timeout"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter { BatchId = 1L }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().BatchId.Should().Be(1L);
    }

    [Fact]
    public async Task GetBatchErrorsAsync_FilterBySeverity_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),   // Info
            MakeError(1L, "Timeout"),         // Warning
            MakeError(1L, "MetadataMissing"), // Critical
            MakeError(1L, null));             // Critical (null → Critical)
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(
            new BatchErrorFilter { Severity = ErrorSeverity.Warning }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().Severity.Should().Be("Warning");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_FilterByConflictType_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),
            MakeError(1L, "Timeout"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(
            new BatchErrorFilter { ConflictType = "Timeout" }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().ConflictType.Should().Be("Timeout");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_SeverityDerivedFromConflictType()
    {
        var (svc, db) = Make();
        db.BatchErrors.Add(MakeError(1L, "DuplicateKey"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter(), default);

        result.Items.Single().Severity.Should().Be("Info");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_NullConflictType_SeverityIsCritical()
    {
        var (svc, db) = Make();
        db.BatchErrors.Add(MakeError(1L, null));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(new BatchErrorFilter(), default);

        result.Items.Single().Severity.Should().Be("Critical");
    }

    [Fact]
    public async Task GetBatchErrorsAsync_Pagination_HonorsPageSize()
    {
        var (svc, db) = Make();
        for (int i = 0; i < 7; i++)
            db.BatchErrors.Add(MakeError(1L, "Timeout"));
        await db.SaveChangesAsync();

        var result = await svc.GetBatchErrorsAsync(
            new BatchErrorFilter { Page = 2, PageSize = 3 }, default);

        result.TotalCount.Should().Be(7);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetBatchErrorByIdAsync_Exists_ReturnsDetailDto()
    {
        var (svc, db) = Make();
        db.BatchErrors.Add(MakeError(1L, "Timeout", eventId: 42L));
        await db.SaveChangesAsync();

        var error = db.BatchErrors.Single();
        var dto   = await svc.GetBatchErrorByIdAsync(error.ErrorId, default);

        dto.Should().NotBeNull();
        dto!.ConflictType.Should().Be("Timeout");
        dto.EventId.Should().Be(42L);
        dto.Severity.Should().Be("Warning");
    }

    [Fact]
    public async Task GetBatchErrorByIdAsync_Missing_ReturnsNull()
    {
        var (svc, db) = Make();
        var dto = await svc.GetBatchErrorByIdAsync(99999L, default);
        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetBatchErrorSummaryAsync_CountsBySeverity()
    {
        var (svc, db) = Make();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),   // Info
            MakeError(1L, "Timeout"),         // Warning
            MakeError(1L, "Deadlock"),        // Warning
            MakeError(1L, "MetadataMissing"), // Critical
            MakeError(1L, null));             // Critical
        await db.SaveChangesAsync();

        var dto = await svc.GetBatchErrorSummaryAsync(null, null, null, default);

        dto.Info.Should().Be(1);
        dto.Warning.Should().Be(2);
        dto.Critical.Should().Be(2);
        dto.Total.Should().Be(dto.Info + dto.Warning + dto.Critical);
    }

    [Fact]
    public async Task GetBatchErrorSummaryAsync_FilterByBatchId_ScopesCounts()
    {
        var (svc, db) = Make();
        db.BatchErrors.AddRange(
            MakeError(1L, "DuplicateKey"),  // batch 1, Info
            MakeError(2L, "Timeout"));       // batch 2, Warning
        await db.SaveChangesAsync();

        var dto = await svc.GetBatchErrorSummaryAsync(1L, null, null, default);

        dto.Info.Should().Be(1);
        dto.Warning.Should().Be(0);
        dto.Total.Should().Be(1);
    }
}
```

- [ ] **Step 4: Run test to verify it fails (compile error)**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build tests\MSOSync.MetadataTests -c Debug
```

Expected: compile error — `BatchErrorQueryService`, `IBatchErrorQueryService` not found.

- [ ] **Step 5: Create interface**

Create `src/MSOSync.Metadata/BatchErrors/IBatchErrorQueryService.cs`:

```csharp
using MSOSync.Metadata.Common;

namespace MSOSync.Metadata.BatchErrors;

public interface IBatchErrorQueryService
{
    Task<PagedResult<BatchErrorSummaryDto>> GetBatchErrorsAsync(
        BatchErrorFilter filter, CancellationToken ct = default);

    Task<BatchErrorDetailDto?> GetBatchErrorByIdAsync(
        long errorId, CancellationToken ct = default);

    Task<BatchErrorSummaryCountDto> GetBatchErrorSummaryAsync(
        long? batchId, DateTime? from, DateTime? to, CancellationToken ct = default);
}
```

- [ ] **Step 6: Implement BatchErrorQueryService**

Create `src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Common;
using MSOSync.Persistence;

namespace MSOSync.Metadata.BatchErrors;

public sealed class BatchErrorQueryService(
    AppDbContext             db,
    IErrorSeverityClassifier classifier) : IBatchErrorQueryService
{
    public async Task<PagedResult<BatchErrorSummaryDto>> GetBatchErrorsAsync(
        BatchErrorFilter filter, CancellationToken ct = default)
    {
        var q = db.BatchErrors.AsNoTracking();

        if (filter.BatchId      is not null) q = q.Where(e => e.BatchId      == filter.BatchId);
        if (filter.ConflictType is not null) q = q.Where(e => e.ConflictType == filter.ConflictType);
        if (filter.From         is not null) q = q.Where(e => e.CreateTime   >= filter.From);
        if (filter.To           is not null) q = q.Where(e => e.CreateTime   <= filter.To);

        if (filter.Severity is not null)
        {
            var types = classifier.GetConflictTypes(filter.Severity.Value);
            q = q.Where(e => types.Contains(e.ConflictType));
        }

        var total = await q.CountAsync(ct);

        // Two-step projection: SQL pulls minimal columns, C# derives Severity
        var rawItems = await q
            .OrderByDescending(e => e.CreateTime)
            .Select(e => new
            {
                e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
                e.ErrorMessage, e.CreateTime, e.RetryCount
            })
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        var items = rawItems
            .Select(e => new BatchErrorSummaryDto(
                e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
                classifier.Classify(e.ConflictType).ToString(),
                e.ErrorMessage, e.CreateTime, e.RetryCount))
            .ToList()
            .AsReadOnly();

        return new PagedResult<BatchErrorSummaryDto>(items, filter.Page, filter.PageSize, total);
    }

    public async Task<BatchErrorDetailDto?> GetBatchErrorByIdAsync(
        long errorId, CancellationToken ct = default)
    {
        var e = await db.BatchErrors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ErrorId == errorId, ct);

        if (e is null) return null;

        return new BatchErrorDetailDto(
            e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
            classifier.Classify(e.ConflictType).ToString(),
            e.ErrorMessage, e.CreateTime, e.RetryCount, e.LastRetryTime);
    }

    public async Task<BatchErrorSummaryCountDto> GetBatchErrorSummaryAsync(
        long? batchId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var baseQ = db.BatchErrors.AsNoTracking();
        if (batchId.HasValue) baseQ = baseQ.Where(e => e.BatchId    == batchId.Value);
        if (from.HasValue)    baseQ = baseQ.Where(e => e.CreateTime >= from.Value);
        if (to.HasValue)      baseQ = baseQ.Where(e => e.CreateTime <= to.Value);

        var infoTypes = classifier.GetConflictTypes(ErrorSeverity.Info);
        var warnTypes = classifier.GetConflictTypes(ErrorSeverity.Warning);
        var critTypes = classifier.GetConflictTypes(ErrorSeverity.Critical);

        int info = await baseQ.CountAsync(e => infoTypes.Contains(e.ConflictType), ct);
        int warn = await baseQ.CountAsync(e => warnTypes.Contains(e.ConflictType), ct);
        int crit = await baseQ.CountAsync(e => critTypes.Contains(e.ConflictType), ct);

        return new BatchErrorSummaryCountDto(info, warn, crit, info + warn + crit);
    }
}
```

- [ ] **Step 7: Run tests**

```powershell
dotnet test tests\MSOSync.MetadataTests -c Debug --filter "FullyQualifiedName~BatchErrorQueryService" --logger "console;verbosity=normal"
```

Expected: all 12 tests PASS.

- [ ] **Step 8: Verify build**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 9: Commit**

```powershell
git add src/MSOSync.Persistence/Entities/SyncBatchError.cs
git add src/MSOSync.Persistence/Configurations/SyncBatchErrorConfiguration.cs
git add src/MSOSync.Metadata/BatchErrors/IBatchErrorQueryService.cs
git add src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs
git add tests/MSOSync.MetadataTests/BatchErrors/BatchErrorQueryServiceTests.cs
git commit -m "feat(9a): add IBatchErrorQueryService + BatchErrorQueryService; add SyncBatchError.CreateTime"
```
