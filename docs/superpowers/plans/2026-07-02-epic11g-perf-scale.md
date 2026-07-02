# Epic 11G: Performance & Scale — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace offset pagination with cursor pagination on the four high-volume stream endpoints, add query cancellation everywhere, and ship a persistent background export job system with a Downloads page.

**Architecture:** Two sequential tracks. Track 1 adds `CursorPageResult<T>` + `CursorToken` to `MSOSync.Common`, updates four query services + controllers + the frontend to use `useInfiniteQuery` with "Load More" UX. Track 2 adds M019 migration (`sync_export_job`), `ExportJobWorker`, `ExportCleanupWorker`, `ExportJobController`, and a Downloads page gated by `EXPORT_DATA` permission with SignalR progress patching.

**Tech Stack:** C# 13 / .NET 9 / EF Core 9 / MediatR 12 / React 19 / TanStack Query v5 `useInfiniteQuery` / SignalR / shadcn / System.Diagnostics.Metrics

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- EF Core 9 — `AsNoTracking()` on all reads; `SaveChangesAsync(ct)` on writes
- No new NuGet packages; no new npm packages
- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use `as const`
- All frontend imports relative — no `@/` aliases
- Build env: `$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"` and `$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"`
- Never `git add -A` or `git add .` — stage files by name
- Never commit `.env` files

---

## File Structure

### New files — backend

```
src/MSOSync.Common/Pagination/CursorPageResult.cs
src/MSOSync.Common/Pagination/CursorToken.cs
src/MSOSync.Persistence/Entities/SyncExportJob.cs
src/MSOSync.Persistence/Configurations/SyncExportJobConfiguration.cs
src/MSOSync.Persistence/Migrations/         — M019_ExportJobs via dotnet ef
src/MSOSync.App/Export/IExportJobService.cs
src/MSOSync.App/Export/ExportJobService.cs
src/MSOSync.App/Export/ExportOptions.cs
src/MSOSync.App/Export/ExportJobChangedNotification.cs
src/MSOSync.App/Workers/ExportJobWorker.cs
src/MSOSync.App/Workers/ExportCleanupWorker.cs
src/MSOSync.App/SignalR/ExportJobChangedPublisher.cs
src/MSOSync.Api/Controllers/ExportJobController.cs
tests/MSOSync.MetadataTests/Pagination/CursorTokenTests.cs
tests/MSOSync.IntegrationTests/Export/ExportJobIntegrationTests.cs
```

### Modified files — backend

```
src/MSOSync.Metadata/Events/EventFilter.cs             — remove Page, add Cursor + IncludeTotalCount
src/MSOSync.Metadata/Events/EventFilterValidator.cs    — remove Page rule
src/MSOSync.Metadata/Events/IEventQueryService.cs      — return type → CursorPageResult<T>
src/MSOSync.Metadata/Events/EventQueryService.cs       — cursor query logic
src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilter.cs
src/MSOSync.Metadata/IncomingBatches/IncomingBatchFilterValidator.cs
src/MSOSync.Metadata/IncomingBatches/IIncomingBatchQueryService.cs
src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs
src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchFilter.cs
src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchFilterValidator.cs
src/MSOSync.Metadata/OutgoingBatches/IOutgoingBatchQueryService.cs
src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchQueryService.cs
src/MSOSync.Metadata/Audit/AuditFilter.cs
src/MSOSync.Metadata/Audit/AuditFilterValidator.cs
src/MSOSync.Metadata/Audit/IAuditQueryService.cs
src/MSOSync.Metadata/Audit/AuditQueryService.cs
src/MSOSync.Metadata/Nodes/INodeQueryService.cs        — add paged overload
src/MSOSync.Metadata/Nodes/NodeQueryService.cs         — add paged implementation
src/MSOSync.Api/Controllers/EventsController.cs        — coordinated with filter change
src/MSOSync.Api/Controllers/IncomingBatchesController.cs
src/MSOSync.Api/Controllers/OutgoingBatchesController.cs
src/MSOSync.Api/Controllers/AuditController.cs
src/MSOSync.Api/Controllers/NodesController.cs         — add pageNumber/pageSize params
src/MSOSync.Persistence/AppDbContext.cs               — add DbSet<SyncExportJob> ExportJobs
src/MSOSync.App/Program.cs                            — register workers + ExportOptions
```

### New files — frontend

```
src/MSOSync.Frontend/src/shared/types/export.ts
src/MSOSync.Frontend/src/shared/api/exportJobs.ts
src/MSOSync.Frontend/src/shared/hooks/useExportJobs.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteEvents.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteIncomingBatches.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteOutgoingBatches.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteAudit.ts
src/MSOSync.Frontend/src/features/downloads/DownloadsPage.tsx
```

### Modified files — frontend

```
src/MSOSync.Frontend/src/shared/types/index.ts         — re-export new types
src/MSOSync.Frontend/src/shared/types/common.ts        — add CursorPageResult<T>
src/MSOSync.Frontend/src/shared/api/events.ts          — cursor params + signal
src/MSOSync.Frontend/src/shared/api/batches.ts         — cursor params + signal
src/MSOSync.Frontend/src/shared/api/audit.ts           — cursor params + signal
src/MSOSync.Frontend/src/shared/api/nodes.ts           — pageNumber/pageSize + signal
src/MSOSync.Frontend/src/shared/queryKeys.ts           — add infinite + exportJobs keys
src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts — add routeExportJobEvent
src/MSOSync.Frontend/src/shared/signalr/types.ts       — add ExportJobEvent type
src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts  — add onExportJobEvent option
src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx — wire ExportJobEvent
src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx   — "All Matching" → create job
src/MSOSync.Frontend/src/features/events/EventsPage.tsx     — useInfiniteEvents
src/MSOSync.Frontend/src/features/events/EventsGrid.tsx     — Load More footer
src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx
src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesGrid.tsx
src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx
src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesGrid.tsx
src/MSOSync.Frontend/src/features/audit/AuditPage.tsx
src/MSOSync.Frontend/src/features/audit/AuditGrid.tsx
src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx       — add pagination state
src/MSOSync.Frontend/src/app/router.tsx               — add /downloads route
src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx    — add Downloads sidebar item
```

---

## Tasks

| # | Name | Deliverable |
|---|---|---|
| 1 | [Cursor pagination — backend](2026-07-02-epic11g-task-1-cursor-backend.md) | CursorPageResult + CursorToken + 4 services + 4 controllers updated + Nodes bounded + unit tests |
| 2 | [Cursor pagination — frontend](2026-07-02-epic11g-task-2-cursor-frontend.md) | 4 useInfinite* hooks + Load More grids + query cancellation + queryKeys + Nodes pagination |
| 3 | [Export job backend](2026-07-02-epic11g-task-3-export-backend.md) | M019 migration + entity + IExportJobService + ExportJobWorker + ExportCleanupWorker + ExportJobController + SignalR publisher + metrics |
| 4 | [Downloads frontend](2026-07-02-epic11g-task-4-downloads-frontend.md) | DownloadsPage + ExportMenu changes + SignalR patch + sidebar wiring |

Execute tasks sequentially: finish Task 1 before Task 2; finish Task 3 before Task 4. Tasks 1-2 and Tasks 3-4 may be developed as two independent tracks (Track 1 fully done before Track 2 starts).
