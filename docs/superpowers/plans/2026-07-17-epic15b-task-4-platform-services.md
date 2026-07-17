# Task 4: Platform Service Migration — IPlatformRepository<SyncAudit>

**Part of:** [Epic 15B Domain Tenant Migration](2026-07-17-epic15b-domain-tenant-migration.md)

**Goal:** After M032, `db.Audits` in an HTTP context is scoped to the current tenant's audits. Admin controllers that show audit history across all operations must use `IPlatformRepository<SyncAudit>` to bypass the global query filter. Update 4 audit services.

**Files to modify:**
- `src/MSOSync.Metadata/Audit/AuditQueryService.cs`
- `src/MSOSync.Metadata/Audit/CorrelationTimelineAssembler.cs`
- `src/MSOSync.Metadata/Audit/AuditSummaryService.cs`
- `src/MSOSync.Metadata/Audit/ExportAuditService.cs` (or wherever it lives — search for `ExportAuditService` if path differs)
- `src/MSOSync.App/Program.cs` — register `IPlatformRepository<SyncAudit>` if not already registered as open-generic

**Interfaces:**
- Consumes: `IPlatformRepository<SyncAudit>` from `MSOSync.Persistence/Tenancy/PlatformRepository.cs`
  - `public interface IPlatformRepository<T> where T : class { IQueryable<T> QueryAll(); }`
- Produces: 4 audit services that read all-tenant audit data safely
- Note: `IPlatformRepository<>` is registered as an open-generic in DI from Task 6 (15A) — no new DI registration needed if the open-generic was registered: `builder.Services.AddScoped(typeof(IPlatformRepository<>), typeof(PlatformRepository<>));`. Verify this is present in `Program.cs`.

---

- [ ] **Step 1: Verify IPlatformRepository<> open-generic is registered**

Open `src/MSOSync.App/Program.cs`. Search for `IPlatformRepository`. Confirm this line exists:

```csharp
builder.Services.AddScoped(typeof(IPlatformRepository<>), typeof(PlatformRepository<>));
```

If it is missing, add it in the tenancy registrations block (near `IHybridLookupService` registration). `IPlatformRepository` and `PlatformRepository` are in `MSOSync.Persistence.Tenancy` — add `using MSOSync.Persistence.Tenancy;` if not already present.

- [ ] **Step 2: Update AuditQueryService**

Open `src/MSOSync.Metadata/Audit/AuditQueryService.cs`.

**Current constructor:**
```csharp
public sealed class AuditQueryService(AppDbContext db, CursorSigner cursorSigner) : IAuditQueryService
```

**New constructor** — add `IPlatformRepository<SyncAudit> auditRepo` parameter:
```csharp
public sealed class AuditQueryService(
    AppDbContext                   db,
    IPlatformRepository<SyncAudit> auditRepo,
    CursorSigner                   cursorSigner) : IAuditQueryService
```

In every method body that queries `db.Audits`, replace `db.Audits` with `auditRepo.QueryAll()`.

Example — `GetAuditsAsync`:
```csharp
// Before:
var query = db.Audits.AsNoTracking()...

// After:
var query = auditRepo.QueryAll()...
```

> `IPlatformRepository<SyncAudit>.QueryAll()` already calls `AsNoTracking()` internally — do not add a second `.AsNoTracking()` call after it, as it would be redundant (though harmless). Check the `PlatformRepository<T>` implementation: `return db.Set<T>().IgnoreQueryFilters().AsNoTracking();` — yes, already includes `AsNoTracking()`.

Add `using MSOSync.Persistence.Entities;` and `using MSOSync.Persistence.Tenancy;` at the top if not present.

- [ ] **Step 3: Update CorrelationTimelineAssembler**

Open `src/MSOSync.Metadata/Audit/CorrelationTimelineAssembler.cs`.

**Current constructor:**
```csharp
public sealed class CorrelationTimelineAssembler(AppDbContext db)
```

**New constructor:**
```csharp
public sealed class CorrelationTimelineAssembler(
    AppDbContext                   db,
    IPlatformRepository<SyncAudit> auditRepo)
```

Replace every `db.Audits` → `auditRepo.QueryAll()` in this class.

If the class uses `db` for entities OTHER than `SyncAudit` (e.g., operations, nodes), keep `db` in the constructor — it is still needed for those queries. Only `db.Audits` changes to `auditRepo.QueryAll()`.

- [ ] **Step 4: Update AuditSummaryService**

Open `src/MSOSync.Metadata/Audit/AuditSummaryService.cs`.

**Current constructor:**
```csharp
public sealed class AuditSummaryService(AppDbContext db) : IAuditSummaryService
```

**New constructor:**
```csharp
public sealed class AuditSummaryService(
    AppDbContext                   db,
    IPlatformRepository<SyncAudit> auditRepo) : IAuditSummaryService
```

Replace `db.Audits` → `auditRepo.QueryAll()` in all method bodies.

- [ ] **Step 5: Update ExportAuditService**

Find the file for `ExportAuditService`. Based on project conventions it is likely at `src/MSOSync.Metadata/Audit/ExportAuditService.cs` or `src/MSOSync.Metadata/Export/ExportAuditService.cs`. If it doesn't exist at either location, run:
```
Get-ChildItem -Recurse -Filter "ExportAuditService.cs" src\
```

**Current constructor:**
```csharp
public sealed class ExportAuditService(AppDbContext db, ICurrentUserService currentUser) : IExportAuditService
```

**New constructor:**
```csharp
public sealed class ExportAuditService(
    AppDbContext                   db,
    IPlatformRepository<SyncAudit> auditRepo,
    ICurrentUserService            currentUser) : IExportAuditService
```

Replace `db.Audits` → `auditRepo.QueryAll()`.

- [ ] **Step 6: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: `Build succeeded. 0 Error(s) 0 Warning(s)`

Common errors to expect and fix:
- `CS0246: The type or namespace name 'IPlatformRepository<>' could not be found` — add `using MSOSync.Persistence.Tenancy;`
- `CS1061: 'AppDbContext' does not contain a definition for 'Audits'` — make sure ALL `db.Audits` usages in each service have been replaced; search for remaining occurrences
- If `AuditQueryService` is registered in DI via `services.AddScoped<IAuditQueryService, AuditQueryService>()`, DI auto-resolves the new `IPlatformRepository<SyncAudit>` constructor parameter via the open-generic registration — no manual DI change needed

- [ ] **Step 7: Run all unit/metadata tests**

```
dotnet test tests/MSOSync.MetadataTests/ -v minimal
dotnet test MSOSync.sln --filter "Category!=Integration" -v minimal
```

Expected: all tests pass.

If audit-related tests fail with "Cannot resolve IPlatformRepository<SyncAudit>", the test fixture creates `AppDbContext` without DI — update audit service test setup to pass a mock or in-memory `IPlatformRepository<SyncAudit>`:

```csharp
// In test setup:
var auditRepo = new TestPlatformRepository<SyncAudit>(db);

// Helper class for tests:
internal sealed class TestPlatformRepository<T>(AppDbContext db) : IPlatformRepository<T>
    where T : class
{
    public IQueryable<T> QueryAll() => db.Set<T>().AsNoTracking();
}
```

Add this helper in the test project's `Helpers/` folder or directly in the test file.

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Metadata/Audit/
git add src/MSOSync.App/Program.cs
git commit -m "feat(15B-4): AuditQueryService, CorrelationTimelineAssembler, AuditSummaryService, ExportAuditService → IPlatformRepository<SyncAudit>"
```
