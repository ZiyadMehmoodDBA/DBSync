# Epic 9A — Operational Read APIs Design

**Status:** FROZEN — approved by CTO 2026-06-24  
**Goal:** Expose read-only HTTP endpoints for Events, IncomingBatches, and BatchErrors so the operations dashboard has a stable, query-optimized API surface before any frontend work begins.  
**Follows:** Epic 8 (Security Hardening + Node Lifecycle) — CTO accepted 2026-06-24  
**Precedes:** Epic 9B (Topology & Health APIs)

---

## 1. Scope

### Included

| Controller | Routes |
|---|---|
| EventsController | `GET /api/v1/events`, `GET /api/v1/events/{eventId}` |
| IncomingBatchesController | `GET /api/v1/incoming-batches`, `GET /api/v1/incoming-batches/{batchId}` |
| BatchErrorsController | `GET /api/v1/batch-errors`, `GET /api/v1/batch-errors/{errorId}`, `GET /api/v1/batch-errors/summary` |

New services: `IEventQueryService`, `IIncomingBatchQueryService`, `IBatchErrorQueryService`, `IErrorSeverityClassifier`  
New migration: `M015_OperationalReadAPIs` (adds `create_time` column + 8 indexes)  
New tests: unit (SQLite) + integration (Testcontainers MsSql)  
Refactor: `PagedResult<T>` moves from `MSOSync.Metadata.Users` → `MSOSync.Metadata.Common`

### Excluded

- Topology, Metrics, Audit, Locks APIs (Epic 9B/9C)
- React Dashboard (Epic 10)
- `IsRetriable` derived field (future enhancement)
- Query metrics / Histogram instrumentation (Epic 9D)
- Load / p99 latency testing (Epic 9D)

---

## 2. Architecture

```
HTTP Request
  → [Authorize(Policy="ViewerOrAbove")]
  → Controller (thin: bind filter, validate, call service, return DTO or throw NotFoundException)
  → IValidator<TFilter>.ValidateAndThrowAsync()    ← in MSOSync.Metadata.*
  → I*QueryService                                  ← in MSOSync.Metadata.*
  → AppDbContext (AsNoTracking + LINQ projection)
  → PagedResult<T> or single DTO
```

No entity objects cross the query service boundary. No EF navigation properties used. No MediatR.

---

## 3. Stack & Global Constraints

- C# 13 / .NET 9 / ASP.NET Core 9 / EF Core 9.0.0
- `TreatWarningsAsErrors = true` — zero warnings
- Central Package Management: never add `Version=` inline in `.csproj`
- FluentValidation 11.11.0
- xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72
- Unit tests: SQLite (`UseSqlite("Data Source=:memory:")`) — NOT EF InMemory provider
- Integration tests: Testcontainers.MsSql 4.4.0
- `AsNoTracking()` on every query — no entity tracking
- No entity objects returned from query services — DTOs only

---

## 4. DTOs

All DTOs are `sealed record` in their respective `MSOSync.Metadata.*` namespace.

### 4.1 Events — `MSOSync.Metadata.Events`

```csharp
sealed record EventSummaryDto(
    long EventId, string TriggerId, string SourceNodeId,
    string ChannelId, char EventType, string TableName,
    long? BatchId,
    DateTime CreateTime, bool IsProcessed);

sealed record EventDetailDto(
    long EventId, string TriggerId, string SourceNodeId,
    string ChannelId, char EventType, string TableName,
    string? PkData, string? RowData, long? TransactionId,
    long? BatchId,
    DateTime CreateTime, bool IsProcessed);
```

`BatchId` is nullable — an event may not have been assigned to any outgoing batch yet.  
`TransactionId` is `long?` — matches `SyncDataEvent.TransactionId` column type.  
`BatchId` source: `MAX(BatchId)` from `sync_data_event_batch` via correlated subquery (OUTER APPLY).

### 4.2 IncomingBatches — `MSOSync.Metadata.IncomingBatches`

```csharp
sealed record IncomingBatchSummaryDto(
    long BatchId, string SourceNodeId, string ChannelId,
    IncomingBatchStatus Status,
    int? RowCount, long BatchSequence,
    DateTime ReceivedTime, long? ApplyTimeMs);

sealed record IncomingBatchDetailDto(
    long BatchId, string SourceNodeId, string ChannelId,
    IncomingBatchStatus Status,
    int? RowCount, long BatchSequence,
    DateTime ReceivedTime, DateTime? LoadTime, DateTime? ExtractTime,
    DateTime? AppliedTime, long? ApplyTimeMs);
```

`Status` uses `IncomingBatchStatus` enum (from `MSOSync.Persistence`) — OpenAPI generates enum names.  
`ApplyTimeMs` in detail projection: computed via `EF.Functions.DateDiffMillisecond(ReceivedTime, AppliedTime.Value)` when `AppliedTime != null`; `null` otherwise. This keeps the response consistent even if the stored `ApplyTimeMs` column is null.

### 4.3 BatchErrors — `MSOSync.Metadata.BatchErrors`

```csharp
sealed record BatchErrorSummaryDto(
    long ErrorId, long BatchId, long? EventId,
    string? ConflictType, string Severity, string? ErrorMessage,
    DateTime CreateTime, int RetryCount);

sealed record BatchErrorDetailDto(
    long ErrorId, long BatchId, long? EventId,
    string? ConflictType, string Severity, string? ErrorMessage,
    DateTime CreateTime, int RetryCount, DateTime? LastRetryTime);

sealed record BatchErrorSummaryCountDto(int Info, int Warning, int Critical, int Total);
```

`Severity` is `string` in the DTO (presentation field). Derived from `ConflictType` via `IErrorSeverityClassifier.Classify()` **in memory after SQL projection** — not in the LINQ query. Serialized via `JsonStringEnumConverter` on the `ErrorSeverity` enum.

### 4.4 Shared — `MSOSync.Metadata.Common`

```csharp
// src/MSOSync.Metadata/Common/PagedResult.cs
namespace MSOSync.Metadata.Common;
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
```

**Refactor required:** Move from `MSOSync.Metadata.Users`. Update `UsersManagementService`, `IUsersManagementService`, and integration tests to use `MSOSync.Metadata.Common.PagedResult<T>`.

---

## 5. Filter Classes

All filters are `sealed class` with `{ get; set; }` properties — required for ASP.NET Core `[FromQuery]` model binding to honor default values. Positional records do not work here.

### 5.1 EventFilter

```csharp
public sealed class EventFilter
{
    public string?   SourceNodeId { get; set; }
    public string?   TriggerId    { get; set; }
    public string?   ChannelId    { get; set; }
    public char?     EventType    { get; set; }
    public bool?     IsProcessed  { get; set; }
    public DateTime? From         { get; set; }
    public DateTime? To           { get; set; }
    public int       Page         { get; set; } = 1;
    public int       PageSize     { get; set; } = 50;
}
```

### 5.2 IncomingBatchFilter

```csharp
public sealed class IncomingBatchFilter
{
    public string?              SourceNodeId { get; set; }
    public string?              ChannelId    { get; set; }
    public IncomingBatchStatus? Status       { get; set; }
    public DateTime?            From         { get; set; }
    public DateTime?            To           { get; set; }
    public int                  Page         { get; set; } = 1;
    public int                  PageSize     { get; set; } = 50;
}
```

### 5.3 BatchErrorFilter

```csharp
public sealed class BatchErrorFilter
{
    public long?          BatchId      { get; set; }
    public string?        ConflictType { get; set; }
    public ErrorSeverity? Severity     { get; set; }
    public DateTime?      From         { get; set; }
    public DateTime?      To           { get; set; }
    public int            Page         { get; set; } = 1;
    public int            PageSize     { get; set; } = 50;
}
```

If both `ConflictType` and `Severity` are supplied, they are ANDed: the `ConflictType` predicate is applied first, then the `Severity` predicate further restricts via `GetConflictTypes()`.

---

## 6. Error Severity Classifier

### 6.1 Types — `MSOSync.Metadata.BatchErrors`

```csharp
public enum ErrorSeverity { Info, Warning, Critical }

public interface IErrorSeverityClassifier
{
    ErrorSeverity Classify(string? conflictType);
    IReadOnlyList<string> GetConflictTypes(ErrorSeverity severity);  // non-nullable
}
```

`Classify(null)` → `ErrorSeverity.Critical`.  
`GetConflictTypes` returns only non-null conflict type strings.  
Registered as `Singleton`.

### 6.2 Severity Mapping (implementation reference)

| ConflictType | Severity |
|---|---|
| `"DuplicateKey"` | Info |
| `"Timeout"` | Warning |
| `"Deadlock"` | Warning |
| `"SequenceGap"` | Warning |
| `"MetadataMissing"` | Critical |
| `null` / unknown | Critical |

Mapping defined as a `FrozenDictionary<string, ErrorSeverity>` in the implementation. Unknown values default to `Critical`.

### 6.3 Future Enhancement (not in Epic 9A)

`IsRetriable` derived field:

| ConflictType | IsRetriable |
|---|---|
| `"DuplicateKey"` | false |
| `"MetadataMissing"` | false |
| `"Timeout"` | true |
| `"Deadlock"` | true |
| `"SequenceGap"` | true |

---

## 7. Query Services

### 7.1 IEventQueryService

```csharp
public interface IEventQueryService
{
    Task<PagedResult<EventSummaryDto>> GetEventsAsync(
        EventFilter filter, CancellationToken ct = default);

    Task<EventDetailDto?> GetEventByIdAsync(
        long eventId, CancellationToken ct = default);
}
```

**List query pattern:**

```csharp
var q = db.DataEvents.AsNoTracking();
if (filter.SourceNodeId is not null) q = q.Where(e => e.SourceNodeId == filter.SourceNodeId);
if (filter.TriggerId    is not null) q = q.Where(e => e.TriggerId    == filter.TriggerId);
if (filter.ChannelId    is not null) q = q.Where(e => e.ChannelId    == filter.ChannelId);
if (filter.EventType    is not null) q = q.Where(e => e.EventType    == filter.EventType);
if (filter.IsProcessed  is not null) q = q.Where(e => e.IsProcessed  == filter.IsProcessed);
if (filter.From         is not null) q = q.Where(e => e.CreateTime   >= filter.From);
if (filter.To           is not null) q = q.Where(e => e.CreateTime   <= filter.To);

int total = await q.CountAsync(ct);
var items = await q
    .OrderByDescending(e => e.CreateTime)
    .Select(e => new EventSummaryDto(
        e.EventId, e.TriggerId, e.SourceNodeId, e.ChannelId, e.EventType, e.TableName,
        db.DataEventBatches.Where(deb => deb.EventId == e.EventId).Max(deb => (long?)deb.BatchId),
        e.CreateTime, e.IsProcessed))
    .Skip((filter.Page - 1) * filter.PageSize)
    .Take(filter.PageSize)
    .ToListAsync(ct);
return new PagedResult<EventSummaryDto>(items.AsReadOnly(), filter.Page, filter.PageSize, total);
```

`BatchId` uses a correlated `MAX` subquery — EF Core translates to `OUTER APPLY (SELECT MAX(batch_id) ...)`. Efficient with `IX_sync_data_event_batch_event_id` present.

**Detail query:** Separate projection from list query. Returns `EventDetailDto?`. Same filter predicate not reused — dedicated `Where(e => e.EventId == eventId)` only.

**Future optimization note:** MVP uses correlated `MAX()`. If performance degrades under high event volume, replace with `LEFT JOIN` + `GROUP BY` projection.

### 7.2 IIncomingBatchQueryService

```csharp
public interface IIncomingBatchQueryService
{
    Task<PagedResult<IncomingBatchSummaryDto>> GetIncomingBatchesAsync(
        IncomingBatchFilter filter, CancellationToken ct = default);

    Task<IncomingBatchDetailDto?> GetIncomingBatchByIdAsync(
        long batchId, CancellationToken ct = default);
}
```

Order: `OrderByDescending(b => b.ReceivedTime)`.  
`Status` filter: `q.Where(b => b.Status == filter.Status)`.

### 7.3 IBatchErrorQueryService

```csharp
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

**Two-step projection for Severity:**

```csharp
// Step 1: SQL projection (no Severity — not translatable to SQL)
var rawItems = await q
    .OrderByDescending(e => e.CreateTime)
    .Select(e => new { e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
                       e.ErrorMessage, e.CreateTime, e.RetryCount })
    .Skip((filter.Page - 1) * filter.PageSize)
    .Take(filter.PageSize)
    .ToListAsync(ct);

// Step 2: in-memory map with Severity derivation
var items = rawItems
    .Select(e => new BatchErrorSummaryDto(
        e.ErrorId, e.BatchId, e.EventId, e.ConflictType,
        _classifier.Classify(e.ConflictType).ToString(),
        e.ErrorMessage, e.CreateTime, e.RetryCount))
    .ToList().AsReadOnly();
```

**Severity filter translation:**

```csharp
if (filter.Severity is not null)
{
    var types = _classifier.GetConflictTypes(filter.Severity.Value);
    q = q.Where(e => types.Contains(e.ConflictType));
}
```

**Summary counts (3 COUNT queries — sufficient for MVP):**

```csharp
var baseQ = db.BatchErrors.AsNoTracking();
if (batchId.HasValue) baseQ = baseQ.Where(e => e.BatchId == batchId.Value);
if (from.HasValue)    baseQ = baseQ.Where(e => e.CreateTime >= from.Value);
if (to.HasValue)      baseQ = baseQ.Where(e => e.CreateTime <= to.Value);

var infoTypes = _classifier.GetConflictTypes(ErrorSeverity.Info);
var warnTypes = _classifier.GetConflictTypes(ErrorSeverity.Warning);
var critTypes = _classifier.GetConflictTypes(ErrorSeverity.Critical);

int info = await baseQ.CountAsync(e => infoTypes.Contains(e.ConflictType), ct);
int warn = await baseQ.CountAsync(e => warnTypes.Contains(e.ConflictType), ct);
int crit = await baseQ.CountAsync(e => critTypes.Contains(e.ConflictType), ct);
return new BatchErrorSummaryCountDto(info, warn, crit, info + warn + crit);
```

**Future optimization note:** 3 separate `COUNT` queries → single `GROUP BY conflict_type` query.

---

## 8. Filter Validators

Validators live in `MSOSync.Metadata.*` (not controllers). Controllers inject `IValidator<TFilter>` and call `ValidateAndThrowAsync` before calling query services.

`ValidateAndThrowAsync` throws `FluentValidation.ValidationException`. `GlobalExceptionHandler` must handle this type → HTTP 400 with RFC 7807 problem details. **If not already handled, add handler arm in M015 scope.**

```csharp
// EventFilterValidator (same shape for IncomingBatch and BatchError)
public sealed class EventFilterValidator : AbstractValidator<EventFilter>
{
    public EventFilterValidator()
    {
        RuleFor(f => f.Page).GreaterThanOrEqualTo(1);
        RuleFor(f => f.PageSize).InclusiveBetween(1, 100);
        RuleFor(f => f.To)
            .GreaterThanOrEqualTo(f => f.From)
            .When(f => f.From.HasValue && f.To.HasValue);
    }
}
```

---

## 9. Controllers

All controllers: `sealed`, `[ApiController]`, `[Authorize(Policy = "ViewerOrAbove")]`.  
No EF access. No business logic. No raw `NotFound()` — use `NotFoundException` from `MSOSync.Common.Exceptions`.

### 9.1 ViewerOrAbove Policy

Add to `SecurityServiceExtensions.AddSecurity()`:

```csharp
options.AddPolicy("ViewerOrAbove", policy => policy.RequireAuthenticatedUser());
```

Any valid JWT qualifies. Named policy enables future role-gating without controller changes.

### 9.2 EventsController

```csharp
[ApiController]
[Route("api/v1/events")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class EventsController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<EventSummaryDto>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetEvents(
        [FromQuery] EventFilter filter, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(filter, ct);
        return Ok(await _events.GetEventsAsync(filter, ct));
    }

    [HttpGet("{eventId:long}")]
    [ProducesResponseType(typeof(EventDetailDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetEventById(long eventId, CancellationToken ct)
    {
        var dto = await _events.GetEventByIdAsync(eventId, ct);
        if (dto is null) throw new NotFoundException($"Event {eventId} not found.");
        return Ok(dto);
    }
}
```

### 9.3 IncomingBatchesController

Same shape as EventsController. Route: `api/v1/incoming-batches`. Id param: `batchId:long`.

### 9.4 BatchErrorsController

`summary` endpoint declared before `{errorId:long}` for readability (route constraint `:long` already prevents ambiguity):

```csharp
[HttpGet("summary")]
[ProducesResponseType(typeof(BatchErrorSummaryCountDto), 200)]
public async Task<IActionResult> GetBatchErrorSummary(
    [FromQuery] long? batchId,
    [FromQuery] DateTime? from,
    [FromQuery] DateTime? to,
    CancellationToken ct)
{
    return Ok(await _batchErrors.GetBatchErrorSummaryAsync(batchId, from, to, ct));
}

[HttpGet("{errorId:long}")]
[ProducesResponseType(typeof(BatchErrorDetailDto), 200)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
public async Task<IActionResult> GetBatchErrorById(long errorId, CancellationToken ct)
{
    var dto = await _batchErrors.GetBatchErrorByIdAsync(errorId, ct);
    if (dto is null) throw new NotFoundException($"BatchError {errorId} not found.");
    return Ok(dto);
}
```

### 9.5 Pagination Contract

Response body only — no `X-Total-Count` header:

```json
{
  "items": [...],
  "page": 1,
  "pageSize": 50,
  "totalCount": 1247
}
```

---

## 10. M015 Migration — `M015_OperationalReadAPIs`

### 10.1 Entity Update

Add to `SyncBatchError`:

```csharp
public DateTime CreateTime { get; set; }
```

Entity configuration:

```csharp
b.Property(e => e.CreateTime)
    .HasColumnName("create_time")
    .HasColumnType("datetime2(7)")
    .HasDefaultValueSql("SYSUTCDATETIME()");
```

### 10.2 Up Migration

**Step 1 — Add `create_time` to `sync_batch_error`:**

```sql
-- 1a: add nullable column
ALTER TABLE [msosync].[sync_batch_error]
    ADD [create_time] datetime2(7) NULL;

-- 1b: backfill existing rows
UPDATE [msosync].[sync_batch_error]
    SET [create_time] = SYSUTCDATETIME()
    WHERE [create_time] IS NULL;

-- 1c: enforce NOT NULL
ALTER TABLE [msosync].[sync_batch_error]
    ALTER COLUMN [create_time] datetime2(7) NOT NULL;

-- 1d: default constraint for new rows
ALTER TABLE [msosync].[sync_batch_error]
    ADD CONSTRAINT [DF_sync_batch_error_create_time]
    DEFAULT SYSUTCDATETIME() FOR [create_time];
```

**Step 2 — Indexes on `sync_data_event`:**

```sql
CREATE INDEX [IX_sync_data_event_source_node_id]
    ON [msosync].[sync_data_event] ([source_node_id]);

CREATE INDEX [IX_sync_data_event_trigger_id]
    ON [msosync].[sync_data_event] ([trigger_id]);

CREATE INDEX [IX_sync_data_event_channel_time]
    ON [msosync].[sync_data_event] ([channel_id], [create_time] DESC);

CREATE INDEX [IX_sync_data_event_create_time]
    ON [msosync].[sync_data_event] ([create_time] DESC);
```

**Step 3 — Indexes on `sync_incoming_batch`:**

```sql
CREATE INDEX [IX_sync_incoming_batch_received_time]
    ON [msosync].[sync_incoming_batch] ([received_time] DESC);

CREATE INDEX [IX_sync_incoming_batch_source_node_time]
    ON [msosync].[sync_incoming_batch] ([source_node_id], [received_time] DESC);

CREATE INDEX [IX_sync_incoming_batch_status_time]
    ON [msosync].[sync_incoming_batch] ([status], [received_time] DESC);
```

**Step 4 — Index on `sync_batch_error`:**

```sql
-- IX_sync_batch_error_batch_id: SKIP — already exists from M005

CREATE INDEX [IX_sync_batch_error_conflict_create]
    ON [msosync].[sync_batch_error] ([conflict_type], [create_time] DESC);
```

**Step 5 — Junction table index (verify first):**

If `sync_data_event_batch` primary key is `(event_id, batch_id)`, the leading column covers `event_id` lookups — skip. If PK is `(batch_id, event_id)` or absent:

```sql
CREATE INDEX [IX_sync_data_event_batch_event_id]
    ON [msosync].[sync_data_event_batch] ([event_id]);
```

Implementer must verify via `sys.indexes` / `sys.index_columns` before executing.

**Step 6 — GlobalExceptionHandler arm for FluentValidation:**

Add handler for `FluentValidation.ValidationException` → HTTP 400 in `GlobalExceptionHandler.cs`.

### 10.3 Index Summary

| Index | Table | Columns | Status |
|---|---|---|---|
| IX_sync_data_event_source_node_id | sync_data_event | source_node_id | NEW |
| IX_sync_data_event_trigger_id | sync_data_event | trigger_id | NEW |
| IX_sync_data_event_channel_time | sync_data_event | channel_id, create_time DESC | NEW |
| IX_sync_data_event_create_time | sync_data_event | create_time DESC | NEW |
| IX_sync_incoming_batch_received_time | sync_incoming_batch | received_time DESC | NEW |
| IX_sync_incoming_batch_source_node_time | sync_incoming_batch | source_node_id, received_time DESC | NEW |
| IX_sync_incoming_batch_status_time | sync_incoming_batch | status, received_time DESC | NEW |
| IX_sync_batch_error_batch_id | sync_batch_error | batch_id | SKIP (M005) |
| IX_sync_batch_error_conflict_create | sync_batch_error | conflict_type, create_time DESC | NEW |
| IX_sync_data_event_batch_event_id | sync_data_event_batch | event_id | VERIFY FIRST |

### 10.4 Down Migration

```sql
-- Reverse order
DROP INDEX IF EXISTS [IX_sync_data_event_batch_event_id] ON [msosync].[sync_data_event_batch];
DROP INDEX IF EXISTS [IX_sync_batch_error_conflict_create] ON [msosync].[sync_batch_error];
DROP INDEX IF EXISTS [IX_sync_incoming_batch_status_time] ON [msosync].[sync_incoming_batch];
DROP INDEX IF EXISTS [IX_sync_incoming_batch_source_node_time] ON [msosync].[sync_incoming_batch];
DROP INDEX IF EXISTS [IX_sync_incoming_batch_received_time] ON [msosync].[sync_incoming_batch];
DROP INDEX IF EXISTS [IX_sync_data_event_create_time] ON [msosync].[sync_data_event];
DROP INDEX IF EXISTS [IX_sync_data_event_channel_time] ON [msosync].[sync_data_event];
DROP INDEX IF EXISTS [IX_sync_data_event_trigger_id] ON [msosync].[sync_data_event];
DROP INDEX IF EXISTS [IX_sync_data_event_source_node_id] ON [msosync].[sync_data_event];

ALTER TABLE [msosync].[sync_batch_error]
    DROP CONSTRAINT [DF_sync_batch_error_create_time];
ALTER TABLE [msosync].[sync_batch_error]
    DROP COLUMN [create_time];
```

---

## 11. DI Registration

All additions to `AddMetadata()` in `MetadataServiceExtensions.cs`:

```csharp
// Query services (Scoped)
services.AddScoped<IEventQueryService, EventQueryService>();
services.AddScoped<IIncomingBatchQueryService, IncomingBatchQueryService>();
services.AddScoped<IBatchErrorQueryService, BatchErrorQueryService>();

// Classifier (Singleton — stateless mapping table)
services.AddSingleton<IErrorSeverityClassifier, ErrorSeverityClassifier>();

// Filter validators (Scoped)
services.AddScoped<IValidator<EventFilter>, EventFilterValidator>();
services.AddScoped<IValidator<IncomingBatchFilter>, IncomingBatchFilterValidator>();
services.AddScoped<IValidator<BatchErrorFilter>, BatchErrorFilterValidator>();
```

---

## 12. File Layout

```
src/MSOSync.Metadata/
  Common/
    PagedResult.cs                          ← moved from Users/; namespace MSOSync.Metadata.Common
  Events/
    EventSummaryDto.cs
    EventDetailDto.cs
    EventFilter.cs
    EventFilterValidator.cs
    IEventQueryService.cs
    EventQueryService.cs
  IncomingBatches/
    IncomingBatchSummaryDto.cs
    IncomingBatchDetailDto.cs
    IncomingBatchFilter.cs
    IncomingBatchFilterValidator.cs
    IIncomingBatchQueryService.cs
    IncomingBatchQueryService.cs
  BatchErrors/
    BatchErrorSummaryDto.cs
    BatchErrorDetailDto.cs
    BatchErrorSummaryCountDto.cs
    BatchErrorFilter.cs
    BatchErrorFilterValidator.cs
    ErrorSeverity.cs
    IErrorSeverityClassifier.cs
    ErrorSeverityClassifier.cs
    IBatchErrorQueryService.cs
    BatchErrorQueryService.cs
  Users/
    PagedResult.cs                          ← DELETE after moving to Common/
    (everything else unchanged)

src/MSOSync.Api/
  Controllers/
    EventsController.cs                     ← new
    IncomingBatchesController.cs            ← new
    BatchErrorsController.cs                ← new
  (Validators live in MSOSync.Metadata.* — not here)

src/MSOSync.App/
  Middleware/
    GlobalExceptionHandler.cs               ← add FluentValidation.ValidationException arm

src/MSOSync.Persistence/
  Entities/
    SyncBatchError.cs                       ← add CreateTime property
  Migrations/
    M015_OperationalReadAPIs.cs             ← new
    M015_OperationalReadAPIs.Designer.cs    ← new
    AppDbContextModelSnapshot.cs            ← update (SyncBatchError + indexes)

tests/MSOSync.MetadataTests/
  Events/
    EventQueryServiceTests.cs               ← new
    EventFilterValidatorTests.cs            ← new
  IncomingBatches/
    IncomingBatchQueryServiceTests.cs       ← new
    IncomingBatchFilterValidatorTests.cs    ← new
  BatchErrors/
    BatchErrorQueryServiceTests.cs          ← new
    BatchErrorFilterValidatorTests.cs       ← new
    ErrorSeverityClassifierTests.cs         ← new

tests/MSOSync.IntegrationTests/
  OperationalRead/
    OperationalReadFixture.cs               ← new (shared Testcontainers + seed data)
    EventsTests.cs                          ← new
    IncomingBatchesTests.cs                 ← new
    BatchErrorsTests.cs                     ← new
```

---

## 13. Testing Strategy

### 13.1 Unit Tests — `tests/MSOSync.MetadataTests`

**ErrorSeverityClassifier (pure unit, no DB):**
- `Classify(null)` → `Critical`
- Round-trip: all types from `GetConflictTypes(X)` classify back to `X`
- `GetConflictTypes(*)` returns no null strings

**Filter validators (pure unit):**
- `Page = 0` → fails
- `PageSize = 101` → fails
- `PageSize = 0` → fails
- `From > To` → fails
- `Page = 1, PageSize = 50` → passes

**Query services (SQLite):**
- Filter by each individual field → only matching rows returned
- Pagination: seed 10 rows, `pageSize=3` → `items.Count=3`, `totalCount=10`
- `GetById`: exists → non-null DTO; missing → null
- `EventQueryService`: event with `DataEventBatch` entry → `BatchId` populated; without → null
- `BatchErrorQueryService`: Severity filter via `GetConflictTypes` → correct rows
- `GetBatchErrorSummaryAsync`: `info + warn + crit = total`
- SQLite note: `char EventType` — verify EF round-trip; add `HasConversion<string>()` in test fixture if needed

### 13.2 Integration Tests — `tests/MSOSync.IntegrationTests/OperationalRead/`

**Shared fixture `OperationalReadFixture`:** Single Testcontainers MsSql instance per class collection. Seeds:
- 5 `SyncDataEvent` (3 processed, 2 not; mixed sourceNodeIds; 2 linked to `DataEventBatch`)
- 3 `SyncIncomingBatch` (1 Applied, 1 Error, 1 New)
- 4 `SyncBatchError` (spanning all 3 severity levels; 2 dated today, 2 dated yesterday)

**EventsTests:**
```
GET /api/v1/events                            → 200, totalCount=5
GET /api/v1/events?isProcessed=true           → 200, totalCount=3
GET /api/v1/events?page=0                     → 400
GET /api/v1/events?pageSize=101               → 400
GET /api/v1/events/{existingId}               → 200, EventDetailDto shape correct
GET /api/v1/events/{nonexistentId}            → 404
GET /api/v1/events (expired JWT)             → 401
GET /api/v1/events (viewer token)            → 200  ← validates ViewerOrAbove policy
```

**IncomingBatchesTests:**
```
GET /api/v1/incoming-batches                  → 200, totalCount=3
GET /api/v1/incoming-batches?status=Error     → 200, totalCount=1
GET /api/v1/incoming-batches/{existingId}     → 200, IncomingBatchDetailDto
GET /api/v1/incoming-batches/{missing}        → 404
GET /api/v1/incoming-batches (expired JWT)   → 401
```

**BatchErrorsTests:**
```
GET /api/v1/batch-errors                      → 200, totalCount=4
GET /api/v1/batch-errors?severity=Warning     → 200, only Warning rows returned
GET /api/v1/batch-errors/{existingId}         → 200, BatchErrorDetailDto
GET /api/v1/batch-errors/{missing}            → 404
GET /api/v1/batch-errors/summary              → 200, Info+Warning+Critical+Total correct
GET /api/v1/batch-errors/summary?batchId=X   → 200, scoped count (only that batch's errors)
GET /api/v1/batch-errors/summary?from=<today> → 200, only today's errors in counts
GET /api/v1/batch-errors (expired JWT)       → 401
```

### 13.3 Performance Baseline (documentation only — not automated in Epic 9A)

```
Target:   100,000+ events → first-page retrieval < 500ms
Indexes:  M015 indexes are prerequisite
Tooling:  k6 or BenchmarkDotNet in Epic 9D
```

---

## 14. CTO-Approved Task Sequence

The spec supports this implementation order (minimizes rework; DB changes stay late until contracts are stable):

```
Task 1  — PagedResult<T> refactor + DTOs + Filter classes + Filter validators
Task 2  — ErrorSeverityClassifier
Task 3  — EventQueryService
Task 4  — IncomingBatchQueryService
Task 5  — BatchErrorQueryService
Task 6  — Controllers (EventsController, IncomingBatchesController, BatchErrorsController)
           + ViewerOrAbove policy in SecurityServiceExtensions
Task 7  — M015 migration (SyncBatchError.CreateTime + indexes + GlobalExceptionHandler arm)
Task 8  — Unit tests (MetadataTests)
Task 9  — Integration tests (IntegrationTests/OperationalRead)
Task 10 — Review + fixes
```

---

## 15. Out of Scope / Future

| Item | Epic |
|---|---|
| `IsRetriable` derived field on BatchErrorDto | Future enhancement |
| Query duration histogram metrics | Epic 9D |
| p99 latency load testing | Epic 9D |
| Topology, Metrics APIs | Epic 9B |
| Audit, Locks APIs | Epic 9C |
| React Dashboard | Epic 10 |
| `BatchId` correlated subquery → LEFT JOIN optimization | Epic 9D if needed |
| Error summary 3×COUNT → single GROUP BY | Epic 9D if needed |
