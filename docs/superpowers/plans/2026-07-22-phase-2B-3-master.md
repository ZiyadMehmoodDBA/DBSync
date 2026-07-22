# Phase 2B.3 — Advanced Operations Analytics Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Four operator-facing analytics modules on top of existing infrastructure — Cluster Operations Dashboard, Configuration Comparison, Audit Explorer, and Operations Timeline — with no new DB migrations.

**Architecture:** `ClusterSummaryQueryService` (Task 1) aggregates 6 parallel queries into a live tactical view; `JsonDiffEngine` (Task 2) flattens and diffs two `SyncConfigurationTemplateVersion.SettingsJson` blobs; `AuditQueryService` (Task 3) gains multi-value `Usernames[]`/`ActionNames[]`/`ObjectNames[]` filtering and `GetEntityHistoryAsync`; `OperationTimelineService` (Task 4) projects `SyncOperation` rows into a Gantt-ready `OperationTimelineDto` with `HasMore` signaling. All 4 modules build on existing EF entities — no new migrations.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / FluentValidation / xUnit + FluentAssertions + Moq / React 19 + TypeScript / Recharts / TanStack Query v5 / lucide-react

Spec: `docs/superpowers/specs/2026-07-22-phase-2B-3-advanced-ops-analytics-design.md`

## Global Constraints

- All Phase 2A rules (`.github/PULL_REQUEST_TEMPLATE.md`): named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3.
- RULE-CTL-2: no controller injects `AppDbContext` directly.
- No new EF migrations (M034 was last).
- All work commits directly to `main`.
- All new query methods: `AsNoTracking()`, project directly to DTO, no lazy loading, no `Include()` unless required.
- All timestamps UTC internally; frontend converts for display only.
- `MSOSync.Metadata` must not reference `MSOSync.Batch` or `MSOSync.Routing`.

---

## Execution Order

| # | Status | Task file | Deliverable |
|---|---|---|---|
| 1 | ⬜ | [Task 1 — Cluster Operations Dashboard](2026-07-22-phase-2B-3-task-1-cluster.md) | `ClusterSummaryQueryService`, `ClusterController`, `ClusterPage.tsx` |
| 2 | ⬜ | [Task 2 — Configuration Comparison](2026-07-22-phase-2B-3-task-2-config-compare.md) | `JsonDiffEngine`, `ConfigurationComparisonService`, `ConfigComparePanel.tsx` |
| 3 | ⬜ | [Task 3 — Audit Explorer](2026-07-22-phase-2B-3-task-3-audit-explorer.md) | Multi-value `AuditFilter`, `GetEntityHistoryAsync`, `AuditFilterBar.tsx` |
| 4 | ⬜ | [Task 4 — Operations Timeline](2026-07-22-phase-2B-3-task-4-timeline.md) | `OperationTimelineService`, timeline endpoint, `TimelinePage.tsx` |
| 5 | ⬜ | [Task 5 — Integration tests + docs](2026-07-22-phase-2B-3-task-5-integration-docs.md) | Integration tests for all 4 modules, docs updates |

Tasks 1–4 are independent and can be executed in any order. Task 5 must be last.

## Completion Criteria

1. All 5 task files complete, committed to `main`.
2. `dotnet test D:\MSOSync\MSOSync.sln` — all unit assemblies green; only accepted environmental integration failures.
3. `npm run build` in `src/MSOSync.Frontend` — 0 TypeScript errors.
4. `docs/architecture/service-responsibility-map.md` updated with new services.
5. `docs/architecture/test-infrastructure.md` updated with new test counts.
