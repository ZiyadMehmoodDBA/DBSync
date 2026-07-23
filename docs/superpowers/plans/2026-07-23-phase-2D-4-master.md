# Phase 2D.4 — 1000-Node Scale Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make MSOSync performant and memory-safe at 1000 registered nodes by adding targeted indexes, cursor pagination for node lists and topology group nodes, projection-only topology queries, a bulk fan-out routing service, dashboard summary caching, and a collapsed batch-error aggregate query.

**Architecture:** Five SQL indexes (migration M038) cover the highest-cost query paths; cursor pagination is added as an additive endpoint (`GET /api/v1/nodes/cursor`) while the existing unbounded endpoint gains a threshold gate; `IBulkRoutingService.FanOutAsync` replaces N individual outgoing-batch inserts with a single `INSERT INTO … SELECT … OUTPUT`; `ClusterSummaryQueryService` and `DashboardQueryService` replace per-bucket `CountAsync` calls with single `GROUP BY` queries; `ITopologyQueryService` gains an optional `nodeIdFilter` overload. BenchmarkDotNet benchmarks (not in CI) verify the wall-clock targets.

**Tech Stack:** C# 13, .NET 9, ASP.NET Core 9, EF Core 9, SQL Server / LocalDB, xUnit 2, FluentAssertions 7, BenchmarkDotNet 0.14, Microsoft.Extensions.Caching.Memory (already in use), IOptions<T> for configuration.

## Global Constraints

- `AsNoTracking()` on all read queries — no exceptions.
- No `Task.WhenAll` on shared `AppDbContext` — sequential `await` only within one scope.
- Cursor values must be opaque base64 — raw `node_id`, `batch_id`, or `event_id` never appear on the wire.
- HMAC verification via `CursorSigner.DecodeString` on all cursor inputs; malformed tokens return HTTP 400, not 500.
- Pagination is additive only — no existing query parameters removed or renamed.
- `GET /api/v1/nodes/paged` gains a `Deprecation: true; rel="successor-version"; url=/api/v1/nodes/cursor` response header but is not removed.
- M038 adds indexes only — no column adds, no table alters, no nullable changes.
- All new/modified controller actions declare `[ProducesResponseType]` for every returned HTTP status code.
- No query materialises more columns than the DTO requires — `Select` to projected type before `ToListAsync`.
- `NodeListCursorThreshold` must come from `IOptions<PaginationOptions>`, not hardcoded.
- Node cursor endpoint clamps `pageSize` to 200; topology group-node endpoint clamps to 500.
- `GET /api/v1/topology/graph?nodeIds=` rejects more than 50 IDs with HTTP 400.
- `IBulkRoutingService.FanOutAsync` uses `ExecuteSqlRawAsync` with parameterised inputs only — no string interpolation in SQL.
- `BulkRoutingService` is registered as scoped; no mutable shared state.
- `CursorToken.EncodeString` / `DecodeString` live in `MSOSync.Common.Pagination` (not only in the Metadata layer) so any future consumer can encode string-keyed cursors without taking a Metadata dependency.
- Migration number is **M038** (M031 already exists for `M031_CoreTopologyTenantId`).

---

## File Map

| File | Action | Task |
|---|---|---|
| `src/MSOSync.Persistence/Migrations/M038_ScaleIndexes.cs` | Create | T1 |
| `src/MSOSync.Common/Pagination/CursorToken.cs` | Modify — add `EncodeString`/`DecodeString` | T2 |
| `src/MSOSync.Metadata/Pagination/CursorSigner.cs` | Modify — add `EncodeString`/`DecodeString` delegates | T2 |
| `src/MSOSync.Metadata/Options/PaginationOptions.cs` | Create | T2 |
| `src/MSOSync.Metadata/NodeManagement/NodeCursorFilter.cs` | Create | T2 |
| `src/MSOSync.Api/Dtos/Nodes/NodeListGateResponse.cs` | Create | T2 |
| `src/MSOSync.Metadata/Interfaces/INodeMetadataService.cs` | Modify — add `GetNodesCursorAsync`, `GetNodesWithGateAsync` | T2 |
| `src/MSOSync.Metadata/Services/NodeMetadataService.cs` | Modify — implement new methods | T2 |
| `src/MSOSync.Api/Controllers/NodesController.cs` | Modify — add cursor endpoint, deprecation header, gate logic | T2 |
| `src/MSOSync.Metadata/MetadataServiceExtensions.cs` | Modify — register `PaginationOptions` | T2 |
| `src/MSOSync.App/appsettings.json` | Modify — add `Pagination:NodeListCursorThreshold` | T2 |
| `tests/MSOSync.MetadataTests/Scale/NodeCursorPaginationTests.cs` | Create | T2 |
| `src/MSOSync.Metadata/Topology/ITopologyQueryService.cs` | Modify — add overloads | T3 |
| `src/MSOSync.Metadata/Topology/TopologyQueryService.cs` | Modify — add `nodeIdFilter`, fix `GetTopologyGraphAsync` projection, fix `GetGroupNodesAsync` cursor | T3 |
| `src/MSOSync.Api/Controllers/TopologyController.cs` | Modify — wire `nodeIds` param; cursor params for group nodes | T3 |
| `src/MSOSync.Metadata/Operations/Cluster/ClusterSummaryQueryService.cs` | Modify — rewrite `QueryNodeStatesAsync` to single GROUP BY | T3 |
| `src/MSOSync.Metadata/Dashboard/DashboardQueryService.cs` | Modify — GROUP BY connectivity_status; snapshot cache | T3 |
| `src/MSOSync.Metadata/Dashboard/DashboardSummaryCache.cs` | Create | T3 |
| `src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs` | Modify — collapse 3 COUNTs to 1 GROUP BY | T3 |
| `tests/MSOSync.MetadataTests/Scale/TopologyOptimizationTests.cs` | Create | T3 |
| `src/MSOSync.Routing/IBulkRoutingService.cs` | Create | T4 |
| `src/MSOSync.Routing/BulkRoutingService.cs` | Create | T4 |
| `src/MSOSync.Routing/RoutingServiceExtensions.cs` | Modify — register `IBulkRoutingService` | T4 |
| `tests/MSOSync.MetadataTests/Scale/BulkRoutingTests.cs` | Create | T4 |
| `src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj` | Create | T5 |
| `src/MSOSync.Benchmarks/BenchmarkDbSeeder.cs` | Create | T5 |
| `src/MSOSync.Benchmarks/TopologyGraphBenchmark.cs` | Create | T5 |
| `src/MSOSync.Benchmarks/NodeCursorPageBenchmark.cs` | Create | T5 |
| `src/MSOSync.Benchmarks/BulkFanOutBenchmark.cs` | Create | T5 |
| `src/MSOSync.Benchmarks/DashboardSummaryBenchmark.cs` | Create | T5 |
| `docs/superpowers/benchmarks/2D-4-baseline.md` | Create (placeholder until first run) | T5 |

---

## Tasks

| # | Name | File |
|---|---|---|
| T1 | M038 Indexes Migration | [2026-07-23-phase-2D-4-task-1-indexes-migration.md](2026-07-23-phase-2D-4-task-1-indexes-migration.md) |
| T2 | Cursor Pagination on Node Endpoints | [2026-07-23-phase-2D-4-task-2-node-cursor-pagination.md](2026-07-23-phase-2D-4-task-2-node-cursor-pagination.md) |
| T3 | Topology + Overview Query Optimization | [2026-07-23-phase-2D-4-task-3-topology-query-optimization.md](2026-07-23-phase-2D-4-task-3-topology-query-optimization.md) |
| T4 | IBulkRoutingService + Tests | [2026-07-23-phase-2D-4-task-4-bulk-routing.md](2026-07-23-phase-2D-4-task-4-bulk-routing.md) |
| T5 | BenchmarkDotNet Project | [2026-07-23-phase-2D-4-task-5-benchmarks.md](2026-07-23-phase-2D-4-task-5-benchmarks.md) |

Tasks T1–T4 are independent once T1 is committed (T2/T3/T4 can proceed in parallel). T5 depends on T2, T3, and T4.

---

## Delivery Checklist

- [ ] `CursorToken.EncodeString` / `DecodeString` added to `MSOSync.Common.Pagination`.
- [ ] `CursorSigner.EncodeString` / `DecodeString` added to `MSOSync.Metadata.Pagination`.
- [ ] `GET /api/v1/nodes/cursor` endpoint implemented returning `CursorPageResult<NodeDto>`.
- [ ] `GET /api/v1/nodes` threshold gate implemented; `NodeListCursorThreshold` configuration key present in `appsettings.json`.
- [ ] `GET /api/v1/nodes/paged` gains `Deprecation` response header.
- [ ] `ITopologyQueryService.GetTopologyGraphAsync(string[]? nodeIdFilter, CancellationToken ct)` overload added.
- [ ] `GetTopologyGraphAsync` uses projection-only node count queries (no full-entity materialisation).
- [ ] `GetGroupNodesAsync` returns `CursorPageResult<TopologyGroupNodeDto>` with `cursor` and `pageSize` parameters.
- [ ] `ClusterSummaryQueryService.QueryNodeStatesAsync` rewritten to single `GROUP BY` query.
- [ ] `DashboardQueryService.GetSummaryAsync` uses single `GROUP BY connectivity_status` for node counts and a snapshot cache.
- [ ] `BatchErrorQueryService.GetBatchErrorSummaryAsync` collapsed from 3 COUNTs to 1 GROUP BY.
- [ ] `IBulkRoutingService` and `BulkRoutingService` implemented in `MSOSync.Routing`.
- [ ] `M038_ScaleIndexes` migration created with all 5 indexes; `Down()` drops all 5.
- [ ] All unit/integration tests pass.
- [ ] `MSOSync.Benchmarks` project created with 4 benchmarks; `docs/superpowers/benchmarks/2D-4-baseline.md` created.
- [ ] No `Task.WhenAll` on shared `AppDbContext` introduced.
- [ ] All new controller actions have `[ProducesResponseType]` for all returned status codes.
