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

| Order | Plan | Workstream | Priority | Audit Findings | Status |
|---|---|---|---|---|---|
| 1 | [2A.8 Configuration](2026-07-17-phase-2A-8-configuration.md) | Configuration typed options | P1 | 2A-004, 2A-005, 2A-006, 2A-007, 2A-008 | Complete |
| 2 | [2A.9 Background Services](2026-07-17-phase-2A-9-background-services.md) | Worker registry compliance | P1 | 2A-009, 2A-010, 2A-011, 2A-012, 2A-013 | Complete |
| 3 | [2A.3 Validation](2026-07-17-phase-2A-3-validation.md) | FluentValidation exclusively | P2 | 2A-002 | Complete |
| 4 | [2A.6 DTO Standardization](2026-07-17-phase-2A-6-dto-standardization.md) | No inline DTOs | P2 | 2A-003, 2A-022 | Complete |
| 5 | [2A.1 API Standardization](2026-07-17-phase-2A-1-api-standardization.md) | ProducesResponseType + no anonymous responses | P2 | 2A-001, 2A-017..2A-020, 2A-024 | Complete |
| 6 | [2A.2 Error Handling](2026-07-17-phase-2A-2-error-handling.md) | Exception hierarchy documentation | P2 | 2A-025..2A-028 | Complete |
| 7 | [2A.4 Dependency Injection](2026-07-17-phase-2A-4-dependency-injection.md) | DI lifetime table + justification comments | P2 | None (audit clean) | Complete |
| 8 | [2A.5 Architecture Consistency](2026-07-17-phase-2A-5-architecture-consistency.md) | Service responsibility map | P2 | 2A-029, 2A-030 | Complete |
| 9 | [2A.7 Logging](2026-07-17-phase-2A-7-logging.md) | Log level guide | P2 | None (audit clean) | Complete |
| 10 | [2A.10 Test Infrastructure](2026-07-17-phase-2A-10-test-infrastructure.md) | Coverage report + gap tests | P2 | 2A-014..2A-016, 2A-023 | Complete |

---

## Audit Backlog Status

Authoritative backlog: `docs/architecture/audit-backlog-2A.md` — all 30 findings
(2A-001..2A-030) resolved: 25 Complete, 4 Accepted, 1 Deferred (2A-029 → Phase 2B).
The snapshot table previously kept here is superseded by that document.

---

## Phase 2A Exit Gate — VERIFIED 2026-07-21

All 10 spec exit criteria satisfied:

1. Every finding resolved (Complete / Accepted / Deferred) — see backlog doc. ✅
2. All P1 findings Fixed (2A-004..2A-013, 2A-025). ✅
3. Stabilization rules documented; PR checklist at `.github/PULL_REQUEST_TEMPLATE.md`. ✅
4. Full-solution `dotnet test` run 2026-07-21: all 12 unit assemblies + Plugin.IntegrationTests
   green (819 passed, 4 skipped); only failures are the 27 accepted environmental
   IntegrationTests failures (2A-014 Docker fixtures, 2A-023 Operations DB login). ✅
5. Coverage baseline generated via coverlet (`coverage-baseline/`, git-ignored) and
   recorded in `docs/architecture/test-infrastructure.md`. ✅
6. Architecture docs committed: api-response-contract, exception-hierarchy, dto-inventory,
   di-lifetime-reference, service-responsibility-map, background-workers,
   worker-config-inventory, logging-guide, test-infrastructure, audit-backlog-2A. ✅
7. No anonymous controller responses (2A-024 sweep). ✅
8. No raw `IConfiguration` in workers (2A.8). ✅
9. No DTOs in controller files (2A.6 + 2A-022 sweep). ✅
10. No singleton→scoped capture without `IServiceScopeFactory` (2A.4 audit clean). ✅

**Phase 2A is complete. Phase 2B may begin.**
