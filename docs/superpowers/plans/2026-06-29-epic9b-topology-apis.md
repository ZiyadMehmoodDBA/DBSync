# Epic 9B: Topology APIs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ITopologyQueryService` + `TopologyController` exposing 5 read-only endpoints (`/graph`, `/summary`, `/groups`, `/groups/{id}`, `/groups/{id}/nodes`) so the React dashboard can render the MSOSync sync network as a React Flow graph.

**Architecture:** Single scoped `TopologyQueryService` following the Epic 9A pattern — thin controller, dedicated query service, 4 DB round-trips for graph, 60-second `IMemoryCache` on structural endpoints. `NodeStatus` enum defined in `MSOSync.Persistence` mapping the 4 persisted uppercase strings. No migrations, no filter classes, no writes.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core 9 / EF Core 9.0.0 / Microsoft.Extensions.Caching.Memory / xUnit 2.9.3 / FluentAssertions 6.12.2 / Moq 4.20.72 / Microsoft.EntityFrameworkCore.Sqlite (unit tests) / LocalDB + WebApplicationFactory<Program> (integration tests)

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings always
- Central Package Management (CPM) — no `Version=` attributes in `.csproj`
- `AsNoTracking()` on every EF query
- No N+1 queries — no per-group or per-router loops that hit the DB
- `GetTopologyGraphAsync` uses exactly 4 DB round-trips
- Cache keys: `"topology:graph:v1"` and `"topology:groups:v1"`, 60-second TTL
- `GetGroupAsync` and `GetGroupNodesAsync` are NOT cached
- Worst-of-members rule for group `ConnectivityStatus`: Unreachable > Degraded > Unknown > Reachable; empty group → Unknown
- All controller endpoints: `[Authorize(Policy = "ViewerOrAbove")]`
- `NodeStatus` enum values match persisted uppercase strings exactly: `PENDING`, `REGISTERED`, `OFFLINE`, `DISABLED`
- Entity `SyncNode.Status` stays as `string` — DTO projection converts via `Enum.Parse<NodeStatus>(n.Status, true)`
- No `TopologyGroupSummaryDto` endpoint (deferred to Epic 10)
- `TopologyMetadataDto.LayoutHint = "dagre"`, `Direction = "TB"`, `Version = 1` (constants)

## Files

```
src/MSOSync.Persistence/
    NodeStatus.cs                              ← new enum (4 values)

src/MSOSync.Metadata/Topology/
    TopologyGraphDto.cs                        ← TopologyGraphDto + TopologyNodeDto + TopologyEdgeDto + TopologyMetadataDto
    TopologyGroupDto.cs                        ← TopologyGroupDto + TopologyGroupNodeDto
    TopologySummaryDto.cs                      ← TopologySummaryDto
    ITopologyQueryService.cs                   ← interface (5 methods)
    TopologyQueryService.cs                    ← implementation (IMemoryCache injected)

src/MSOSync.Api/Controllers/
    TopologyController.cs                      ← 5 endpoints, thin

src/MSOSync.Metadata/
    MetadataServiceExtensions.cs               ← add ITopologyQueryService registration

tests/MSOSync.MetadataTests/Topology/
    TopologyQueryServiceTests.cs               ← 12 SQLite unit tests

tests/MSOSync.IntegrationTests/Topology/
    TopologyFixture.cs                         ← fixture + [CollectionDefinition("Topology")]
    TopologyTests.cs                           ← 8 integration tests [Collection("Topology")]
```

## Tasks

- [Task 1](2026-06-29-epic9b-task-1-dtos-enum.md) — `NodeStatus` enum + all 3 DTO files; build clean
- [Task 2](2026-06-29-epic9b-task-2-query-service.md) — `ITopologyQueryService` + `TopologyQueryService` + 12 SQLite unit tests; build + tests green
- [Task 3](2026-06-29-epic9b-task-3-controller-tests.md) — `TopologyController` + DI wire + integration tests; full suite green; commit
