# Phase 2D.5 — Adaptive Polling + Pipeline Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate fixed-cadence polling waste and per-batch compression overhead by introducing adaptive per-node polling intervals, a compression threshold gate, parallel channel dispatch, and a metrics sink for pipeline stage timing.

**Architecture:** `AdaptivePollingOrchestrator` (a new `BackgroundService`) replaces `SyncJob`'s fixed `PeriodicTimer`, managing one long-running `Task` per active node that sleeps for the interval computed by `AdaptivePollingService`; per-node state is kept in `IMemoryCache`. `NodeHttpClient` gains a compression threshold gate backed by `ICompressionService` (abstraction over the existing `GzipCompressionService`) and a new `BrotliCompressionService`; `SyncEngine.RunAsync` dispatches cross-node batches in parallel using `Task.WhenAll` with one `IServiceScope` per node group. `IMetricsService` (registered in `MSOSync.Common`) provides a thread-safe in-memory histogram sink for five named pipeline stage timings.

**Tech Stack:** C# 13 / .NET 9, `Microsoft.Extensions.Caching.Memory` (`IMemoryCache`), `System.IO.Compression` (GZip + Brotli — BCL, no new packages), xUnit, FluentAssertions, Moq.

## Global Constraints

- `IAdaptivePollingService` lives in `MSOSync.Scheduler` namespace; no dependency on `MSOSync.Transport` or `MSOSync.Engine`.
- Adaptive polling state is stored in `IMemoryCache` only — no EF entity, no DB table, no new migration.
- Parallel channel dispatch: one `IServiceScope` per node group; `DbContext` never crosses task boundaries.
- `CompressionOptions.ThresholdBytes` always read from `IOptions<CompressionOptions>` — never hardcoded.
- `IMetricsService` default implementation is `InMemoryMetricsService`: `ConcurrentDictionary<string, ConcurrentQueue<double>>`, ring-buffer cap 1 000 entries per histogram.
- No new EF migrations introduced by 2D.5.
- `BrotliCompressionService` uses `System.IO.Compression.BrotliStream` (BCL .NET 9 — no NuGet).
- `SyncJob.RunTickAsync` gains optional `string? nodeId = null`; parameterless call-site in existing tests remains valid.
- `PullJob` is NOT touched — it keeps its fixed `PullIntervalSeconds` timer.
- `IWorkerStatusRegistry` registration for `SyncJob` moves to `AdaptivePollingOrchestrator`; `SyncJob` no longer self-registers.
- Jitter uses `Random.Shared` — never `new Random()`.
- All new interfaces and options classes are `sealed` (where applicable) or `interface` only — no abstract base classes.
- `AdaptivePollingOrchestrator` handles zero active nodes at startup gracefully.
- xUnit + FluentAssertions + Moq across all test files.
- **Dependency note:** Per-node state uses `IMemoryCache` (not `ICacheService` from 2D.1). If 2D.1 ships first and introduces `ICacheService`, the implementation can be updated to use it; for now `IMemoryCache` is the direct dependency.

---

## File Map

### New files

| Path | Responsibility |
|---|---|
| `src/MSOSync.Common/IMetricsService.cs` | Interface: `RecordHistogram`, `IncrementCounter` |
| `src/MSOSync.Common/InMemoryMetricsService.cs` | Default impl: thread-safe ring-buffer per histogram |
| `src/MSOSync.Scheduler/IAdaptivePollingService.cs` | Interface: `GetIntervalAsync`, `RecordActivityAsync`, `RecordErrorAsync`, `ResetAsync` |
| `src/MSOSync.Scheduler/AdaptivePollingService.cs` | Impl: algorithm from spec; uses `IMemoryCache` |
| `src/MSOSync.Scheduler/NodePollingState.cs` | `internal sealed record` with 5 fields |
| `src/MSOSync.Scheduler/Options/AdaptivePollingOptions.cs` | Bound from `"AdaptivePolling"` config section |
| `src/MSOSync.Scheduler/AdaptivePollingOrchestrator.cs` | `BackgroundService`; one `Task` per node; replaces fixed timer in `SyncJob` |
| `src/MSOSync.Transport/ICompressionService.cs` | Interface: `EncodingName`, `Compress`, `Decompress` |
| `src/MSOSync.Transport/BrotliCompressionService.cs` | Brotli impl |
| `src/MSOSync.Transport/CompressionOptions.cs` | Bound from `"Compression"` config section |
| `src/MSOSync.Transport/ICompressionNegotiator.cs` | Interface: `SelectFor(nodeId)` |
| `src/MSOSync.Transport/CompressionNegotiator.cs` | Reads `IMemoryCache` key `node-compression:{nodeId}`; brotli > gzip fallback |
| `tests/MSOSync.SchedulerTests/AdaptivePollingServiceTests.cs` | Unit tests for the adaptive algorithm |
| `tests/MSOSync.TransportTests/CompressionServiceTests.cs` | Round-trip + level tests |
| `tests/MSOSync.TransportTests/CompressionGateTests.cs` | Threshold gate tests via `NodeHttpClient` |
| `tests/MSOSync.TransportTests/CompressionNegotiatorTests.cs` | Negotiator selection tests |

### Modified files

| Path | Change summary |
|---|---|
| `src/MSOSync.Transport/GzipCompressionService.cs` | Implement `ICompressionService`; use `CompressionOptions` level |
| `src/MSOSync.Transport/NodeHttpClient.cs` | Replace `GzipCompressionService` ctor param with `ICompressionNegotiator` + `IOptions<CompressionOptions>`; add threshold gate; add `IMetricsService` timing |
| `src/MSOSync.Transport/TransportServiceExtensions.cs` | Register `ICompressionService`, `BrotliCompressionService`, `ICompressionNegotiator`, `CompressionOptions`; remove direct `GzipCompressionService` singleton; accept `IConfiguration` |
| `src/MSOSync.Transport/SmartTransportService.cs` | Add `IMetricsService`; instrument `send_ms` and `ack_ms` |
| `src/MSOSync.Transport/AcknowledgementService.cs` | Add `IMetricsService`; instrument `ack_ms` |
| `src/MSOSync.Engine/SyncEngine.cs` | Parallel dispatch via `Task.WhenAll` + `IServiceScopeFactory`; instrument `fetch_ms` and `send_ms` via `IMetricsService` |
| `src/MSOSync.Engine/SyncEngineExtensions.cs` | Pass `IServiceScopeFactory` through DI to `SyncEngine` (it's already available, just needs ctor injection) |
| `src/MSOSync.Scheduler/SyncJob.cs` | Demote from `BackgroundService` to plain class; remove `ExecuteAsync`; add `string? nodeId` param to `RunTickAsync`; remove `IWorkerStatusRegistry` self-register from `StartAsync` |
| `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` | Remove `AddHostedService<SyncJob>()`; add `AddScoped<SyncJob>()`; register `IAdaptivePollingService` + `AdaptivePollingOrchestrator` |
| `src/MSOSync.App/appsettings.json` | Add `"AdaptivePolling"` and `"Compression"` sections |
| `tests/MSOSync.SchedulerTests/SyncJobTests.cs` | Update `BuildJob()` — `SyncJob` no longer takes `IOptions<SyncOptions>` or `IWorkerStatusRegistry` in its ctor (registry moves to orchestrator) |
| `tests/MSOSync.TransportTests/GzipCompressionServiceTests.cs` | Update to construct via `ICompressionService` (interface) |

---

## Tasks

- [Task 1](2026-07-23-phase-2D-5-task-1-metrics-and-compression-abstraction.md) — `IMetricsService` + `InMemoryMetricsService` + `ICompressionService` abstraction (gzip + brotli)
- [Task 2](2026-07-23-phase-2D-5-task-2-adaptive-polling-service.md) — `IAdaptivePollingService` + `AdaptivePollingService` + options + unit tests
- [Task 3](2026-07-23-phase-2D-5-task-3-orchestrator-and-syncjob-demotion.md) — `AdaptivePollingOrchestrator` + `SyncJob` demotion + `SyncSchedulerExtensions` wiring
- [Task 4](2026-07-23-phase-2D-5-task-4-pipeline-optimization-and-integration.md) — Parallel dispatch in `SyncEngine`, compression gate in `NodeHttpClient`, metrics instrumentation, `appsettings.json`, integration tests

---

## Execution Order

Tasks must run sequentially: Task 1 → Task 2 → Task 3 → Task 4.
Task 1 produces `IMetricsService` and `ICompressionService` that Tasks 3 and 4 depend on.
Task 2 produces `IAdaptivePollingService` that Task 3 depends on.
