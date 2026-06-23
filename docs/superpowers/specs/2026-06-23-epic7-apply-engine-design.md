# Epic 7: Apply Engine — Design Spec

**Date:** 2026-06-23  
**Status:** Approved  
**CTO Rating:** 9.5/10

---

## Overview

Epic 7 delivers the SQL apply engine for MSOSync CE. It replaces `NoOpApplyService` with a production-grade `ApplyEngine` that reconstructs INSERT, UPDATE, and DELETE statements from event payloads and executes them against the target database using raw ADO.NET. Events carry full row data (`row_data`) and primary key data (`pk_data`); PK column lists are stored in a new `pk_columns_json` column on `sync_trigger` (M013).

---

## Section 1: Architecture

### File Structure

```
MSOSync.Engine/
  Contracts/
    IApplyService.cs              ← moved from MSOSync.Transport
    ApplyResult.cs                ← moved from MSOSync.Transport
    ISqlConnectionFactory.cs
    IApplyFailureClassifier.cs
    ApplyFailureCategory.cs
    ITriggerApplyMetadataService.cs
    ISqlEventApplicator.cs
  Apply/
    ApplyEngine.cs                ← implements IApplyService
    ApplyContext.cs               ← internal record
    TriggerApplyMetadata.cs
  Sql/
    SqlEventApplicator.cs         ← orchestrates builders, implements ISqlEventApplicator
    InsertBuilder.cs
    UpdateBuilder.cs
    DeleteBuilder.cs
    SqlStatement.cs
  Metadata/
    TriggerApplyMetadataService.cs  ← implements ITriggerApplyMetadataService
  ServiceCollectionExtensions.cs
```

### Key Principles

- Raw ADO.NET (`SqlConnection`/`SqlCommand`/`SqlTransaction`) for all DML — no EF Core for SQL execution
- `ISqlConnectionFactory` decouples connection creation from EF Core context lifecycle
- `SqlEventApplicator` and sub-builders are pure SQL generators — no I/O, no state
- Events grouped by `TriggerId`; trigger metadata prefetched once per batch as `Dictionary<string, TriggerApplyMetadata>`
- `IClock` injected everywhere; no `DateTime.UtcNow` or `DateTimeOffset.UtcNow` directly
- `AddApplyEngine()` extension method on `IServiceCollection` for self-contained DI registration

---

## Section 2: Data Model & Migration Changes

### M013 Migration

Adds `pk_columns_json` to `sync_trigger`:

```sql
ALTER TABLE sync_trigger
    ADD pk_columns_json nvarchar(max) NULL;
```

Nullable for backward compatibility. Triggers registered before Epic 7 have `NULL` and `TriggerVersion=1`. Future migration can add `NOT NULL` after trigger rebuild tooling exists.

### SyncTrigger Entity

New property:

```csharp
public string? PkColumnsJson { get; set; }
```

No `[NotMapped]` convenience property on entity — deserialization happens once at batch-level in `TriggerApplyMetadataService`.

### TriggerVersion

`TriggerVersion` increments from `1` → `2` for Epic 7-compatible triggers. Apply engine checks `TriggerVersion < 2` → `MetadataMissing` row-level error.

### Trigger DDL Changes

`SqlServerTriggerBuilder.BuildTriggerSql()` gains `IReadOnlyList<string> pkColumns` parameter. Updated trigger body captures `pk_data` for all three event types:

| Event | `pk_data` source | `row_data` source |
|-------|-----------------|-----------------|
| INSERT | `inserted` (PK cols only) | `inserted` (full row) |
| UPDATE | `deleted` (PK cols only, pre-update) | `inserted` (full row, post-update) |
| DELETE | `deleted` (PK cols only) | `NULL` |

Generated SQL fragment for PK capture (supports composite keys):

```sql
SELECT [tenant_id],[order_id] FROM inserted FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
```

This handles composite PKs producing `{"tenant_id":1,"order_id":42}` without special-casing.

### pk_hash Column (Deferred)

`pk_hash varchar(128) NULL` on `sync_data_event` — noted for a future migration. Not required for Epic 7 but reserved for duplicate detection and conflict resolution in later epics.

---

## Section 3: ApplyEngine Internals

### TriggerApplyMetadata

```csharp
public sealed record TriggerApplyMetadata(
    string                SchemaName,
    string                TableName,
    IReadOnlyList<string> PkColumns,
    int                   TriggerVersion);
```

`SchemaName` required because `dbo.orders` ≠ `sales.orders` ≠ `archive.orders`.

### ApplyContext (internal)

```csharp
internal sealed record ApplyContext(
    SqlConnection                          Connection,
    SqlTransaction                         Transaction,
    Dictionary<string, TriggerApplyMetadata> Metadata,
    CancellationToken                      CancellationToken);
```

Passed through apply pipeline to avoid long parameter lists.

### ISqlConnectionFactory

```csharp
public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct);
}
```

`SqlConnectionFactory` reads `ConnectionStrings:Default`. Separate from EF Core connection — same database, separate pool entry.

### IApplyFailureClassifier / ApplyFailureCategory

```csharp
public enum ApplyFailureCategory
{
    DuplicateKey,        // SqlException 2627, 2601   — row-level, continue
    RowNotFound,         // 0 rows affected            — row-level, continue
    FKViolation,         // SqlException 547           — row-level, continue
    MetadataMissing,     // TriggerVersion < 2 or null — row-level, continue
    SerializationError,  // malformed pk_data/row_data — row-level, continue
    Deadlock,            // SqlException 1205          — fatal, rollback
    Timeout,             // SqlException -2            — fatal, rollback
    SyntaxError,         // SqlException 102, 208      — fatal, rollback
    Unknown              // all others                 — fatal, rollback
}
```

Row-level categories: `DuplicateKey`, `RowNotFound`, `FKViolation`, `MetadataMissing`, `SerializationError` → `SyncBatchError` inserted, `ErrorRows++`, continue.  
Fatal categories: `Deadlock`, `Timeout`, `SyntaxError`, `Unknown` → rollback batch, status=`Error`, return immediately.

### ISqlEventApplicator / SqlStatement

```csharp
public sealed record SqlStatement(
    string                    CommandText,
    IReadOnlyList<SqlParameter> Parameters);

public interface ISqlEventApplicator
{
    SqlStatement BuildInsert(string schemaName, string tableName, JsonElement rowData);
    SqlStatement BuildUpdate(string schemaName, string tableName, JsonElement pkData, JsonElement rowData);
    SqlStatement BuildDelete(string schemaName, string tableName, JsonElement pkData);
}
```

`SqlEventApplicator` orchestrates `InsertBuilder`, `UpdateBuilder`, `DeleteBuilder`. All builders are stateless singletons. Column names derived from `rowData.EnumerateObject()` at runtime — no `columns_json` metadata column needed.

### ApplyEngine.ApplyAsync — Execution Flow

1. Extract distinct `TriggerId` values from `payload.Events`
2. Prefetch `TriggerApplyMetadata` for those IDs → `Dictionary<string, TriggerApplyMetadata>`
3. Open `SqlConnection` via `ISqlConnectionFactory`, begin `SqlTransaction`
4. For each `EventPayload` in `payload.Events`:
   a. Lookup metadata by `TriggerId`
   b. If missing or `TriggerVersion < 2` → `MetadataMissing` row error, `ErrorRows++`, continue
   c. Parse `pk_data` / `row_data` JSON; failure → `SerializationError` row error, continue
   d. `SAVE TRANSACTION event_{eventId}` savepoint
   e. Build SQL via `ISqlEventApplicator`
   f. Execute command; catch `SqlException` → classify via `IApplyFailureClassifier`
   g. Row-level failure → `ROLLBACK TRANSACTION event_{eventId}`, add `SyncBatchError`, `ErrorRows++`, continue
   h. Fatal failure → rollback entire transaction, set batch to `Error`, return
5. Commit transaction
6. Status: `ErrorRows == 0` → `Applied`; `AppliedRows > 0` → `PartialSuccess`; else `Error`
7. `finally` block: update `SyncIncomingBatch.Status` + `AppliedTime` via EF Core — guaranteed even on fatal failure

### Savepoint Rationale

`SAVE TRANSACTION` / `ROLLBACK TRANSACTION event_x` ensures a row-level FK violation or duplicate key does not doom the batch transaction (`XACT_ABORT` interaction). Each row gets an independent savepoint; the outer transaction commits successfully even with per-row rollbacks.

### Metrics

Inside `ApplyEngine`, emit:

| Metric | Labels |
|--------|--------|
| `msosync_apply_rows_total` | `node_id`, `channel_id`, `status` |
| `msosync_apply_errors_total` | `node_id`, `channel_id`, `conflict_type` |
| `msosync_apply_batches_total` | `node_id`, `channel_id`, `status` |
| `msosync_apply_duration_seconds` | `node_id`, `channel_id` |
| `msosync_apply_partial_batches_total` | `node_id`, `channel_id` |

Avoid high-cardinality labels (`batch_id`, `trigger_id`).

---

## Section 4: Interface Moves & DI Wiring

### Files Moving Out of MSOSync.Transport

| File | Action |
|------|--------|
| `IApplyService.cs` | Move to `MSOSync.Engine/Contracts/` |
| `ApplyResult.cs` | Move to `MSOSync.Engine/Contracts/` |
| `NoOpApplyService.cs` | Delete |

`MSOSync.Transport` already references `MSOSync.Engine` — no circular dependency introduced.

### ServiceCollectionExtensions.cs

```csharp
public static IServiceCollection AddApplyEngine(this IServiceCollection services)
{
    services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
    services.AddSingleton<IApplyFailureClassifier, SqlApplyFailureClassifier>();
    services.AddSingleton<InsertBuilder>();
    services.AddSingleton<UpdateBuilder>();
    services.AddSingleton<DeleteBuilder>();
    services.AddScoped<ISqlEventApplicator, SqlEventApplicator>();
    services.AddScoped<ITriggerApplyMetadataService, TriggerApplyMetadataService>();
    services.AddScoped<IApplyService, ApplyEngine>();
    return services;
}
```

`Program.cs` call:

```csharp
builder.Services.AddApplyEngine();
```

Removes old `NoOpApplyService` registration.

### MSOSync.Engine.csproj — New Package Reference

```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.2" />
```

Explicit pin; EF Core pulls this transitively but pinning prevents version drift.

---

## Section 5: Testing Strategy

### MSOSync.EngineTests (New Project)

Unit tests. No Testcontainers. SQLite for EF Core tests (same pattern as MetadataTests).

**Packages:**
```xml
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="FluentAssertions" Version="6.12.2" />
<PackageReference Include="Moq" Version="4.20.72" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
<PackageReference Include="coverlet.collector" Version="6.0.2" />
```

**InsertBuilderTests — scenarios:**
- Basic single-column insert
- Multi-column insert
- Null values
- Strings with quotes
- DateTime values
- Decimal values
- Bool values
- GUID values
- Empty JSON object
- Unsupported JSON token type → throws `SerializationException`

**UpdateBuilderTests — scenarios:**
- Single PK + single SET column
- Composite PK `["tenant_id","order_id"]` → `WHERE [tenant_id]=@pk0 AND [order_id]=@pk1`
- Multiple SET columns
- PK column in row_data (SET and WHERE both present)

**DeleteBuilderTests — scenarios:**
- Single PK
- Composite PK
- Empty `pk_data` → throws `ArgumentException`

**SqlApplyFailureClassifierTests — error number mapping:**

| Error Number | Expected Category |
|-------------|------------------|
| 2627 | `DuplicateKey` |
| 2601 | `DuplicateKey` |
| 547 | `FKViolation` |
| 1205 | `Deadlock` |
| -2 | `Timeout` |
| 102 | `SyntaxError` |
| 208 | `SyntaxError` |
| 99999 | `Unknown` |

**TriggerApplyMetadataServiceTests (SQLite):**
- Fetches metadata by trigger ID
- Returns null for unknown trigger ID
- Handles malformed `pk_columns_json` → throws `MetadataException` (contract TBD — pick one and make it explicit)
- Batch prefetch returns only requested IDs (no over-fetching)

**ApplyEngine: NOT unit-tested** — ADO.NET mocking (SqlConnection/SqlCommand/SqlTransaction) is non-interface and brittle. Covered entirely by integration tests.

### MSOSync.IntegrationTests/Engine/ (Existing Project, New Subfolder)

Full stack against Testcontainers MsSql. Seeds a real `test_orders` table and `sync_trigger` row with `TriggerVersion=2`, `PkColumnsJson='["order_id"]'`.

**Test scenarios:**

| Test | Scenario | Assert |
|------|----------|--------|
| `InsertEvent_AppliesRow` | INSERT event | Row exists in `test_orders` |
| `UpdateEvent_ModifiesRow` | UPDATE event | Row values updated |
| `DeleteEvent_RemovesRow` | DELETE event | Row absent |
| `DuplicateKey_PartialSuccess` | INSERT on existing PK | `ErrorRows=1`, status=`PartialSuccess`, `SyncBatchError` present |
| `FKViolation_Savepoint_ContinuesBatch` | Event 1 ok, event 2 FK violation, event 3 ok | Events 1+3 committed; savepoint isolated event 2; `ErrorRows=1`, status=`PartialSuccess` |
| `MetadataMissing_TriggerVersionLow` | `TriggerVersion=1` | `ConflictType.MetadataMissing`, status=`PartialSuccess` |
| `FatalDeadlock_RollsBackBatch` | Simulated deadlock (error 1205) | Entire batch rolled back, status=`Error` |
| `SyntaxError_RollsBackBatch` | Bad SQL (table dropped mid-batch) | 0 rows committed, status=`Error` |
| `CompositePk_Update_Works` | `pk_columns_json='["tenant_id","order_id"]'`, UPDATE | WHERE uses both PK columns; correct row updated |
| `ReplayBatch_DuplicateInsert_PartialSuccess` | Same batch applied twice | First: `Applied`; second: `PartialSuccess`, `ErrorRows=N`, no rollback |
| `MultiTrigger_MetadataPrefetch` | 100 events across 2 triggers | Exactly 1 metadata query (verified via EF query interceptor) |
| `LargeBatch_10000Rows_AppliesSuccessfully` | 10,000 INSERT events | All rows committed, correct count, no OOM |
| `CancellationToken_CancelsApply` | Cancel mid-batch | Transaction rolled back, `OperationCanceledException` propagated |

**Coverage targets:**

| Component | Target |
|-----------|--------|
| `InsertBuilder`, `UpdateBuilder`, `DeleteBuilder` | 95% |
| `SqlApplyFailureClassifier` | 100% |
| `TriggerApplyMetadataService` | 90% |
| `ApplyEngine` | Integration only |
| Overall `MSOSync.Engine` | 85%+ |

---

## Out of Scope (Later Epics)

- `pk_hash` column on `sync_data_event` (Epic 8+)
- `PostgresApplicator` / `OracleApplicator` / `BulkApplyEngine` (Epic 10+)
- Task 9 controllers that set `pk_columns_json` when registering triggers (Monday work)
- Trigger rebuild tooling for migrating `TriggerVersion=1` triggers to `TriggerVersion=2`
- Making `pk_columns_json NOT NULL` (after trigger rebuild tooling ships)
