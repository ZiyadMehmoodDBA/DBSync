# Epic 11D: Export + Audit Intelligence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add streaming CSV/JSON export to every server-paginated table and add audit intelligence visualizations to the Audit page.

**Architecture:** Two independent tracks in four tasks — backend first (Tasks 1–2), frontend second (Tasks 3–4). Task 1 adds a generic `IExportService<TFilter>` streaming layer injected into existing controllers. Task 2 adds a summary query service for audit analytics. Tasks 3–4 wire these into the React UI with an `ExportMenu` dropdown, a failure dialog, and eight ApexCharts visualizations behind a new "Insights" tab on the Audit page.

**Tech Stack:** C# 13 / .NET 9 / EF Core 9 / FluentValidation 11 / xUnit 2.9 / FluentAssertions 6 / React 19 / TanStack Query v5 / AG Grid / ApexCharts 5 / shadcn Tabs

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true` — no warnings allowed
- EF Core 9.0.0 — use `AsNoTracking()` and `AsAsyncEnumerable()` for streaming; no `ToListAsync()` in export paths
- No new NuGet packages — use `System.Text.Json.Utf8JsonWriter` for JSON streaming; hand-roll CSV (`StreamWriter` + `CsvHelper.Escape`)
- No new npm packages — ApexCharts already installed (`apexcharts ^5.15.2`, `react-apexcharts ^2.1.1`)
- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use string union types or `as const` objects
- All frontend imports relative (no `@/` aliases)
- Auth policy on all new backend endpoints: `"ViewerOrAbove"`
- `IExportService<TFilter>.ExportCsvAsync` and `.ExportJsonAsync` return `Task<int>` (row count)
- `StreamingExportResult` writes directly to `Response.Body`; never buffers full result set
- Export audit records stored in `sync_audit`: `ActionName = "EXPORT_{RESOURCE}"`, `ObjectName = "{resource}|{format}|{rowCount}|{durationMs}"` (fits varchar(100))
- `DayBucket` must include zero-count days to ensure continuous chart x-axis
- Unit tests: SQLite in-memory via `TestDbContext.Create()` (NOT EF InMemory provider)
- xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72

---

## File Structure

### New files — backend
```
src/MSOSync.Metadata/Export/
  IExportService.cs              — generic streaming interface (Task 1)
  CsvHelper.cs                   — static Escape(string?) helper (Task 1)
  IExportAuditService.cs         — audit write interface (Task 1)
  ExportAuditService.cs          — writes to sync_audit (Task 1)
  EventExportService.cs          — streams SyncDataEvent rows (Task 1)
  IncomingBatchExportService.cs  — streams IncomingBatch rows (Task 1)
  OutgoingBatchExportService.cs  — streams OutgoingBatch rows (Task 1)
  AuditExportService.cs          — streams SyncAudit rows (Task 1)

src/MSOSync.Metadata/Audit/
  AuditSummaryDto.cs             — summary DTO + bucket records (Task 2)
  IAuditSummaryService.cs        — summary interface (Task 2)
  AuditSummaryService.cs         — grouped EF queries (Task 2)

src/MSOSync.Api/Results/
  StreamingExportResult.cs       — IActionResult writing to Response.Body (Task 1)
```

### Modified files — backend
```
src/MSOSync.Api/Controllers/EventsController.cs        — add export action (Task 1)
src/MSOSync.Api/Controllers/IncomingBatchesController.cs — add export action (Task 1)
src/MSOSync.Api/Controllers/BatchController.cs         — add export action (Task 1)
src/MSOSync.Api/Controllers/AuditController.cs         — add export + summary actions (Tasks 1, 2)
src/MSOSync.Metadata/MetadataServiceExtensions.cs      — register new services (Tasks 1, 2)
```

### New files — tests
```
tests/MSOSync.MetadataTests/Export/
  EventExportServiceTests.cs     — CSV header, filter, JSON validity, CSV escape (Task 1)
  AuditExportServiceTests.cs     — same pattern for audit (Task 1)
tests/MSOSync.MetadataTests/Audit/
  AuditSummaryServiceTests.cs    — totals, day buckets, zero fill, by user (Task 2)
```

### New files — frontend
```
src/MSOSync.Frontend/src/shared/
  api/export.ts                  — downloadExport(), downloadCurrentViewCsv(), downloadCurrentViewJson() (Task 3)
  hooks/useExport.ts             — isExporting state, all-rows/view dispatch, failure state (Task 3)
  components/ExportMenu.tsx      — dropdown: Current View / All Matching Rows (Task 3)
  components/ExportFailureDialog.tsx — Retry / Export Current View (Task 3)
  types/audit-summary.ts         — AuditSummaryDto + bucket types (Task 4)

src/MSOSync.Frontend/src/features/audit/
  AuditInsightsTab.tsx           — date presets + 8 charts (Task 4)
```

### Modified files — frontend
```
src/MSOSync.Frontend/src/shared/api/audit.ts           — add getAuditSummary() (Task 4)
src/MSOSync.Frontend/src/shared/queryKeys.ts           — add auditSummary key (Task 4)
src/MSOSync.Frontend/src/shared/types/index.ts         — export audit-summary types (Task 4)
src/MSOSync.Frontend/src/features/events/EventsPage.tsx — add ExportMenu (Task 3)
src/MSOSync.Frontend/src/features/audit/AuditPage.tsx  — add ExportMenu + tabs + Insights (Tasks 3, 4)
src/MSOSync.Frontend/src/features/incoming-batches/*   — add ExportMenu (Task 3)
src/MSOSync.Frontend/src/features/outgoing-batches/*   — add ExportMenu (Task 3)
```

---

## Tasks

| # | Name | Deliverable |
|---|------|-------------|
| 1 | [Backend export streaming](2026-07-01-epic11d-task-1-backend-export-streaming.md) | IExportService + 4 impls + StreamingExportResult + 4 controller actions + tests |
| 2 | [Audit summary backend](2026-07-01-epic11d-task-2-audit-summary-backend.md) | AuditSummaryService + GET /audit/summary + tests |
| 3 | [Frontend export UX](2026-07-01-epic11d-task-3-frontend-export-ux.md) | useExport hook + ExportMenu + ExportFailureDialog + 4 page integrations |
| 4 | [Audit visualizations](2026-07-01-epic11d-task-4-audit-visualizations.md) | AuditInsightsTab (8 charts) + tabs on AuditPage |
