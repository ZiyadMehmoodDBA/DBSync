# Epic 9C: Metrics APIs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `IMetricsQueryService` + `MetricsController` exposing 5 read-only endpoints (`/summary`, `/nodes`, `/channels`, `/runtime`, `/monitors`) so the React dashboard can display operational health and DBA tooling can query raw diagnostic snapshots.

**Architecture:** Single scoped `MetricsQueryService` following the Epic 9A pattern — thin controller, dedicated query service, 30-second `IMemoryCache` on the three primary endpoints (`summary`, `nodes`, `channels`). Diagnostic endpoints (`runtime`, `monitors`) not cached. No migrations, no filter classes, no writes. `BatchStatus.Acknowledged = 2` (byte) is the confirmed terminal state for `SyncOutgoingBatch.Status` — non-terminal = `Status != 2`.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core 9 / EF Core 9.0.0 / Microsoft.Extensions.Caching.Memory / xUnit 2.9.3 / FluentAssertions 6.12.2 / Moq 4.20.72 / Microsoft.EntityFrameworkCore.Sqlite (unit tests) / LocalDB + WebApplicationFactory<Program> (integration tests)

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings always
- Central Package Management (CPM) — no `Version=` attributes in `.csproj`
- `AsNoTracking()` on every EF query
- No N+1 queries — no per-node or per-channel sub-queries inside loops
- Cache keys: `"metrics:summary:v1"`, `"metrics:nodes:v1"`, `"metrics:channels:v1"`, 30-second TTL
- `GetRuntimeMetricsAsync` and `GetMonitorMetricsAsync` are NOT cached
- Fixed 24-hour window: `DateTime cutoff = DateTime.UtcNow.AddHours(-24)`
- All controller endpoints: `[Authorize(Policy = "ViewerOrAbove")]`
- `OutgoingQueueDepth` = `SyncOutgoingBatch` rows WHERE `Status != 2` (2 = `BatchStatus.Acknowledged`)
- `SyncRuntimeStats` has no `NodeId` field — `GET /metrics/runtime` returns all rows, no nodeId filter
- `SyncBatchError.BatchId` is FK to `SyncOutgoingBatch.BatchId` — join via `OutgoingBatches` to get `NodeId`/`ChannelId`
- No filter classes, no FluentValidation validators, no pagination — full lists returned

## Files

```
src/MSOSync.Metadata/Metrics/
    MetricsSummaryDto.cs               ← new (Task 1)
    NodeMetricsDto.cs                  ← new (Task 1)
    ChannelMetricsDto.cs               ← new (Task 1)
    RuntimeMetricsDto.cs               ← new (Task 1)
    MonitorMetricDto.cs                ← new (Task 1)
    IMetricsQueryService.cs            ← new interface (Task 2)
    MetricsQueryService.cs             ← new implementation (Task 2)

src/MSOSync.Api/Controllers/
    MetricsController.cs               ← new (Task 3)

src/MSOSync.Metadata/
    MetadataServiceExtensions.cs       ← add IMetricsQueryService registration (Task 3)

tests/MSOSync.MetadataTests/Metrics/
    MetricsQueryServiceTests.cs        ← 12 SQLite unit tests (Task 2)

tests/MSOSync.IntegrationTests/Metrics/
    MetricsFixture.cs                  ← fixture + [CollectionDefinition("Metrics")] (Task 3)
    MetricsTests.cs                    ← 7 integration tests [Collection("Metrics")] (Task 3)
```

## Tasks

- [Task 1](2026-06-29-epic9c-task-1-dtos.md) — 5 DTO files in `MSOSync.Metadata/Metrics/`; build clean
- [Task 2](2026-06-29-epic9c-task-2-query-service.md) — `IMetricsQueryService` + `MetricsQueryService` + 12 SQLite unit tests; build + tests green
- [Task 3](2026-06-29-epic9c-task-3-controller-tests.md) — `MetricsController` + DI wire + integration tests; full suite green; commit
