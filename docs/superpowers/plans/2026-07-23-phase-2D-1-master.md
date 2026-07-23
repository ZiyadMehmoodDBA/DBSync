# Phase 2D.1 — Cache Abstraction + Redis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all direct `IMemoryCache` usage in nine Metadata services with a unified `ICacheService` abstraction that lives in `MSOSync.Common` and supports pluggable Memory and Redis providers.

**Architecture:** `ICacheService` and both implementations (`InMemoryCacheService`, `RedisCacheService`) live entirely in `MSOSync.Common`. Provider selection is configuration-driven via `Cache:Provider` in `appsettings.json`. `MSOSync.Metadata` and `MSOSync.Api` never reference `StackExchange.Redis` directly — they only see `ICacheService`. `RoutingService` retains its direct `IMemoryCache` dependency because it uses `CancellationChangeToken`-based mass eviction.

**Tech Stack:** .NET 9, C# 13, `StackExchange.Redis` 2.8.16, `System.Text.Json`, xUnit, Moq, FluentAssertions, `Testcontainers.Redis` 4.4.0, `NetArchTest.Rules`

## Global Constraints

- No EF migrations — `ICacheService` is infrastructure only, no database schema changes.
- No new projects — all cache code lives in `MSOSync.Common`.
- `IMemoryCache` retained for `RoutingService` — `CancellationChangeToken` eviction model is irreplaceable via `ICacheService`.
- Both `ICacheService` implementations are singletons. Scoped services may safely receive a singleton.
- `StackExchange.Redis` package referenced only by `MSOSync.Common` — no other project.
- Serialization: `System.Text.Json` camelCase only. No Newtonsoft.Json.
- All cache keys generated via `CacheKeyHelper` static methods — no hard-coded strings in callers.
- Redis does not fall back to memory on failure — Redis errors are logged at Warning, operations degrade to cache-miss behavior.
- `RemoveByPrefixAsync` throws `NotSupportedException` on the Memory provider (no callers in this phase).
- `IConnectionMultiplexer` is singleton; never create per-request.
- Health check (`redis-cache`) registered only when `Provider=Redis`.
- xUnit + FluentAssertions for all tests.

---

## File Map

| File | Action | Task |
|---|---|---|
| `Directory.Packages.props` | Add `StackExchange.Redis` 2.8.16 + `Testcontainers.Redis` 4.4.0 | T2 |
| `src/MSOSync.Common/MSOSync.Common.csproj` | Add `StackExchange.Redis` + `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` package refs | T1 |
| `src/MSOSync.Common/Caching/ICacheService.cs` | Create | T1 |
| `src/MSOSync.Common/Caching/CacheOptions.cs` | Create | T1 |
| `src/MSOSync.Common/Caching/CacheKeyHelper.cs` | Create | T1 |
| `src/MSOSync.Common/Caching/InMemoryCacheService.cs` | Create | T1 |
| `src/MSOSync.Common/Caching/CachingExtensions.cs` | Create (Memory branch only in T1; Redis branch added in T2) | T1, T2 |
| `tests/MSOSync.MetadataTests/Caching/InMemoryCacheServiceTests.cs` | Create | T1 |
| `src/MSOSync.Common/Caching/RedisCacheService.cs` | Create | T2 |
| `src/MSOSync.Common/Caching/RedisCacheHealthCheck.cs` | Create | T2 |
| `tests/MSOSync.MetadataTests/Caching/RedisCacheServiceTests.cs` | Create | T2 |
| `tests/MSOSync.IntegrationTests/Caching/RedisCacheIntegrationTests.cs` | Create | T2 |
| `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj` | Add `Testcontainers.Redis` ref | T2 |
| `src/MSOSync.Metadata/Overview/OverviewSnapshotCache.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Topology/TopologyQueryService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Metrics/MetricsQueryService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Permissions/PermissionService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Services/NodeMetadataService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Services/ChannelMetadataService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Services/TriggerMetadataService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Services/RouterMetadataService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.Metadata/Services/ParameterMetadataService.cs` | Modify — Tier A migration | T3 |
| `src/MSOSync.App/Program.cs` | Add `AddCacheService(builder.Configuration)` call | T3 |
| `tests/MSOSync.MetadataTests/Topology/TopologyQueryServiceTests.cs` | Update — replace `IMemoryCache` construction with `ICacheService` mock | T3 |
| `tests/MSOSync.MetadataTests/Metrics/MetricsQueryServiceTests.cs` | Update — replace `IMemoryCache` construction with `ICacheService` mock | T3 |
| `tests/MSOSync.MetadataTests/Permissions/PermissionServiceTests.cs` | Update — replace `IMemoryCache` with `ICacheService` mock | T3 |
| `tests/MSOSync.MetadataTests/ChannelMetadataServiceTests.cs` | Update — replace `IMemoryCache` with `ICacheService` mock | T3 |
| `tests/MSOSync.MetadataTests/RouterMetadataServiceTests.cs` | Update — replace `IMemoryCache` with `ICacheService` mock | T3 |
| `tests/MSOSync.MetadataTests/TriggerMetadataServiceTests.cs` | Update — replace `IMemoryCache` with `ICacheService` mock | T3 |
| `tests/MSOSync.MetadataTests/NodeMetadataServiceTests.cs` | Update — replace `IMemoryCache` with `ICacheService` mock | T3 |
| `tests/MSOSync.MetadataTests/ParameterMetadataServiceTests.cs` | Update — replace `IMemoryCache` with `ICacheService` mock | T3 |
| `tests/MSOSync.ArchTests/DependencyTests.cs` | Add Redis isolation tests | T4 |

---

## Tasks

- [Task 1: ICacheService + InMemoryCacheService + CacheKeyHelper + DI](2026-07-23-phase-2D-1-task-1-cache-abstraction.md)
- [Task 2: RedisCacheService + RedisCacheHealthCheck + Integration Tests](2026-07-23-phase-2D-1-task-2-redis.md)
- [Task 3: Migrate Tier A Callers](2026-07-23-phase-2D-1-task-3-migrate-callers.md)
- [Task 4: Architecture Tests + Integration Wiring](2026-07-23-phase-2D-1-task-4-arch-tests.md)
