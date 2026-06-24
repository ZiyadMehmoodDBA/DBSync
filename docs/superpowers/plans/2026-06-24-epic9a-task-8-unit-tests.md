# Task 8: Comprehensive Unit Test Pass

**Part of:** [Epic 9A Plan](2026-06-24-epic9a-operational-read-apis.md)

**Goal:** Run all MetadataTests to confirm full coverage across Tasks 1–5. Fill any gaps in coverage. Verify the full suite passes with zero failures.

**Files:**
- Test: `tests/MSOSync.MetadataTests/` — all existing test files from Tasks 1–5

**Note:** All test files were written in Tasks 1–5 using TDD. This task verifies they all pass together, catches any regressions introduced across tasks, and adds any missing edge cases.

---

- [ ] **Step 1: Run full MetadataTests suite**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests\MSOSync.MetadataTests -c Debug --logger "console;verbosity=normal"
```

Expected: all tests PASS. Count should include:
- 6 EventFilterValidatorTests
- 4 IncomingBatchFilterValidatorTests
- 4 BatchErrorFilterValidatorTests
- 10 ErrorSeverityClassifierTests
- 10 EventQueryServiceTests
- 7 IncomingBatchQueryServiceTests
- 12 BatchErrorQueryServiceTests
- All pre-existing tests (NodeMetadataService, ParameterMetadataService, etc.)

Total: ~53+ tests all green.

- [ ] **Step 2: If any test fails, diagnose and fix**

Common issues:

**SQLite FK constraint failure on IncomingBatchQueryService tests:**
`SyncIncomingBatch` has a FK to `SyncNode` via `source_node_id`. The SQLite test DB has FK enforcement disabled by default in EF Core SQLite. If tests pass, no action needed. If you see `FOREIGN KEY constraint failed`, add to `TestDbContext.Create()`:
```csharp
db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
```
after `db.Database.OpenConnection()`.

**SyncDataEventBatch missing in TestDbContext:**
If `EventQueryService` tests fail with `no such table: sync_data_event_batch`, the table is included in `AppDbContext` as `DataEventBatches`. It should be created by `EnsureCreated()`. Verify `AppDbContext` includes `public DbSet<SyncDataEventBatch> DataEventBatches => Set<SyncDataEventBatch>();` — it does (confirmed in AppDbContext lines already read).

**PagedResult<T> namespace resolution:**
If existing tests like `UsersManagementServiceTests` fail with ambiguous type, add `using MSOSync.Metadata.Common;` to those files. The old `MSOSync.Metadata.Users.PagedResult<T>` was deleted in Task 1.

- [ ] **Step 3: Verify full solution still builds clean**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit any fixes (only if changes were needed)**

```powershell
# Only run if Step 2 required fixes
git add <changed files>
git commit -m "fix(9a): resolve unit test issues after Tasks 1-7"
```

If no fixes needed, skip this step — no empty commits.
