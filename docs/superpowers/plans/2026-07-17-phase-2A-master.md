# Phase 2A — Platform Stabilization Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stabilize the MSOSync architecture baseline before any Phase 2B+ feature work begins.

**Architecture:** Audit-and-fix pass across 10 workstreams. Each workstream has its own plan file with isolated scope, concrete tasks, and exit criteria matching the spec's Definition of Complete. Workstreams are ordered by priority: P1 first (configuration, background services), then P2 cleanup, then documentation, then testing.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / FluentValidation / Serilog / MediatR / Entity Framework Core / React 19 / TypeScript

## Global Constraints

- No new product features during Phase 2A. Scope is strictly stabilization and consistency.
- Every audit finding must have an explicit resolution (Fixed / Deferred / Accepted) before Phase 2A exits.
- Definition of Complete: implementation merged + `dotnet test` exits 0 + no new rule violations + docs updated.
- The `appsettings.Development.json` must never contain plaintext credentials. Use environment variables.
- Breaking API changes require API versioning strategy before implementation (none planned for Phase 2A).
- Spec: `docs/superpowers/specs/2026-07-17-phase-2A-platform-stabilization.md`

---

## Execution Order

Execute workstream plans in this order. P1 items (2A.8, 2A.9) must complete before any other workstream begins. P2 items (2A.1–2A.7) can execute in any order after P1 is complete. 2A.10 runs last.

| Order | Plan | Workstream | Priority | Audit Findings |
|---|---|---|---|---|
| 1 | [2A.8 Configuration](2026-07-17-phase-2A-8-configuration.md) | Configuration typed options | P1 | 2A-004, 2A-005, 2A-006, 2A-007, 2A-008 |
| 2 | [2A.9 Background Services](2026-07-17-phase-2A-9-background-services.md) | Worker registry compliance | P1 | 2A-009, 2A-010, 2A-011, 2A-012 |
| 3 | [2A.3 Validation](2026-07-17-phase-2A-3-validation.md) | FluentValidation exclusively | P2 | 2A-002 |
| 4 | [2A.6 DTO Standardization](2026-07-17-phase-2A-6-dto-standardization.md) | No inline DTOs | P2 | 2A-003 |
| 5 | [2A.1 API Standardization](2026-07-17-phase-2A-1-api-standardization.md) | ProducesResponseType + no anonymous responses | P2 | 2A-001 |
| 6 | [2A.2 Error Handling](2026-07-17-phase-2A-2-error-handling.md) | Exception hierarchy documentation | P2 | None (audit clean) |
| 7 | [2A.4 Dependency Injection](2026-07-17-phase-2A-4-dependency-injection.md) | DI lifetime table + justification comments | P2 | None (audit clean) |
| 8 | [2A.5 Architecture Consistency](2026-07-17-phase-2A-5-architecture-consistency.md) | Service responsibility map | P2 | None (audit clean) |
| 9 | [2A.7 Logging](2026-07-17-phase-2A-7-logging.md) | Log level guide | P2 | None (audit clean) |
| 10 | [2A.10 Test Infrastructure](2026-07-17-phase-2A-10-test-infrastructure.md) | Coverage report + gap tests | P2 | N/A |

---

## Audit Backlog Status

Full backlog in: `docs/architecture/audit-backlog-2A.md` (created by 2A.1 plan, Task 1)

| ID | Finding | Workstream | Priority | Status |
|---|---|---|---|---|
| 2A-001 | ExportJobController returns anonymous `new { jobId }` on 202 | 2A.1 | P2 | Not Started |
| 2A-002 | PreferencesController manual key validation instead of FluentValidation | 2A.3 | P2 | Not Started |
| 2A-003 | `CreateExportJobRequest`, `ExportJobDto` defined inline in controller file | 2A.6 | P2 | Not Started |
| 2A-004 | HeartbeatWorker uses `IConfiguration.GetValue("Heartbeat:IntervalSeconds")` | 2A.8 | P1 | Not Started |
| 2A-005 | ProbeWorker uses `IConfiguration.GetValue("Heartbeat:ProbeIntervalSeconds")` | 2A.8 | P1 | Not Started |
| 2A-006 | ConnectivityEvaluator uses raw IConfiguration for heartbeat/probe intervals | 2A.8 | P1 | Not Started |
| 2A-007 | PullJob uses `IConfiguration.GetValue("Sync:PullIntervalSeconds")` | 2A.8 | P1 | Not Started |
| 2A-008 | SyncJob uses `IConfiguration.GetValue("Sync:IntervalSeconds")` | 2A.8 | P1 | Not Started |
| 2A-009 | SyncJob missing IWorkerStatusRegistry | 2A.9 | P1 | Not Started |
| 2A-010 | PullJob missing IWorkerStatusRegistry | 2A.9 | P1 | Not Started |
| 2A-011 | RetryJob missing IWorkerStatusRegistry, interval hardcoded | 2A.9 | P1 | Not Started |
| 2A-012 | PurgeJob missing IWorkerStatusRegistry, uses Task.Delay loop | 2A.9 | P1 | Not Started |

---

## Phase 2A Exit Gate

After all workstream plans complete, all 10 exit criteria in the spec must be verified. Do not declare Phase 2A complete until every item in the spec's Exit Criteria section is satisfied.

Run final verification:
```
dotnet test D:\MSOSync\MSOSync.sln
```
Expected: 0 failures.
