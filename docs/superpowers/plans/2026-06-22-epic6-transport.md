# Epic 6: Transport Layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the full node-to-node transport layer (PULL and PUSH modes), replacing the `NoOpTransportService` stub from Epic 5 with a working `SmartTransportService`, `PushClient`, `PullClient`, `SyncController`, `PullJob`, and apply stub.

**Architecture:** `SmartTransportService` reads `SyncNode.TransportMode` from `INodeMetadataService` cache (60 s TTL) and dispatches PUSH via `PushClient` or no-op for PULL targets. `SyncController` (4 endpoints) is always active, guarded by `NodeTokenAuthMiddleware`. `PullJob` self-disables if local node is in PUSH mode. `NoOpApplyService` transitions batches through `New→Applying→Applied`; Epic 7 replaces that single registration with real SQL.

**Tech Stack:** C# 13 · .NET 9 · ASP.NET Core · EF Core 9 · `Microsoft.Extensions.Http` · Polly 8 · `System.IO.Compression` · xUnit 2.9.3 · FluentAssertions 6.12.2 · Moq 4.20.72 · Testcontainers.MsSql 4.4.0 · SQLite (unit tests)

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`, `LangVersion = 13.0`
- EF Core 9.0.0 — no raw SQL except migrations and explicit spec callouts
- All `DateTime` via `IClock.UtcNow`; `DateTimeOffset` only for wire-protocol AckTime field
- `MSOSync.Transport` must NOT inject `AppDbContext` directly — use interfaces from Batch/Metadata
- `NodeToken` must NEVER appear in logs, HTTP responses, config dumps, or `appsettings.json`
- Transport endpoints at `/api/v1/sync/*` guarded by `NodeTokenAuthMiddleware` (no JWT)
- Node identity on inbound requests from `context.User` claim `"nodeId"` — never trust payload node IDs
- Unit tests use SQLite in-memory (`Microsoft.EntityFrameworkCore.Sqlite`), NOT EF InMemory provider
- Integration tests use Testcontainers.MsSql 4.4.0 (require Docker)
- Stage files by name only — never `git add .` or `git add -A`
- Spec: `docs/superpowers/specs/2026-06-22-epic6-transport-design.md`

---

## Task Summary

| # | Task | Key Files | Brief |
|---|------|-----------|-------|
| 1 | M012 + Enums + NodeProperties + Entity/Dto updates | `Persistence/TransportMode.cs`, `Persistence/IncomingBatchStatus.cs`, `SyncNode.cs`+, `SyncIncomingBatch.cs`+, `SyncNodeConfiguration.cs`+, `SyncIncomingBatchConfiguration.cs`+, `Common/NodeProperties.cs`, `Metadata/Dtos/NodeDto.cs`+, `NodeMetadataService.cs`+ | [task1](2026-06-22-epic6-task1-m012-entities.md) |
| 2 | BatchStatus rename + named IBatchStateMachine + IClock injection | `Batch/BatchStatus.cs`, `Batch/IBatchStateMachine.cs`, `Batch/BatchStateMachine.cs`, `Scheduler/SchedulerRecovery.cs`+, `tests/EngineTests/BatchStateMachineTests.cs`+ | [task2](2026-06-22-epic6-task2-batchstatus-statemachine.md) |
| 3 | Transport scaffolding: wire DTOs + GzipCompressionService + failure types | `Transport/Payloads/EventPayload.cs`, `BatchPayload.cs`, `PullRequest.cs`, `PullResponse.cs`, `AckPayload.cs`, `PushResponse.cs`, `PingResponse.cs`, `Transport/GzipCompressionService.cs`, `TransportFailureReason.cs`, `ITransportFailureClassifier.cs`, `TransportFailureClassifier.cs`, `TransportJsonContext.cs`; delete `Batch/GzipBatchCompressor.cs` | [task3](2026-06-22-epic6-task3-transport-scaffolding.md) |
| 4 | INodeHttpClient + Polly | `Transport/INodeHttpClient.cs`, `Transport/NodeHttpClient.cs` | [task4](2026-06-22-epic6-task4-nodehttpclient.md) |
| 5 | IBatchTransportQueryService (in Batch) | `Batch/IBatchTransportQueryService.cs`, `Batch/BatchTransportQueryService.cs` | [task5](2026-06-22-epic6-task5-batch-query-service.md) |
| 6 | SmartTransportService + AcknowledgementService | `Transport/SmartTransportService.cs`, `Transport/AcknowledgementService.cs` | [task6](2026-06-22-epic6-task6-smart-transport-ack.md) |
| 7 | PushClient + PullClient | `Transport/PushClient.cs`, `Transport/PullClient.cs` | [task7](2026-06-22-epic6-task7-push-pull-clients.md) |
| 8 | IApplyService + update ITransportService + SyncEngine | `Transport/IApplyService.cs`, `Transport/ApplyResult.cs`, `Transport/NoOpApplyService.cs`, `Engine/ITransportService.cs`+, `Engine/SyncEngine.cs`+, delete `Engine/NoOpTransportService.cs`, `Engine/SyncEngineExtensions.cs`+ | [task8](2026-06-22-epic6-task8-apply-itransport-update.md) |
| 9 | SyncController (4 endpoints) | `Api/Controllers/SyncController.cs` | [task9](2026-06-22-epic6-task9-sync-controller.md) |
| 10 | ITopologyService + TopologyService + PullJob | `Topology/ITopologyService.cs`, `Topology/SourceNodeInfo.cs`, `Topology/TopologyService.cs`, `Topology/TopologyServiceExtensions.cs`, `Scheduler/PullJob.cs`, `Scheduler/MSOSync.Scheduler.csproj`+ | [task10](2026-06-22-epic6-task10-topology-pulljob.md) |
| 11 | DI wiring + csproj updates | `Transport/TransportServiceExtensions.cs`, `Batch/BatchPipelineExtensions.cs`+, `Scheduler/SyncSchedulerExtensions.cs`+, `App/Program.cs`+, `App/MSOSync.App.csproj`+, `Transport/MSOSync.Transport.csproj`+ | [task11](2026-06-22-epic6-task11-di-wiring.md) |
| 12 | Unit tests (MSOSync.TransportTests) | New project `tests/MSOSync.TransportTests/` — 5 test classes | [task12](2026-06-22-epic6-task12-unit-tests.md) |
| 13 | SchedulerRecovery Sending recovery + Integration tests | `Scheduler/SchedulerRecovery.cs`+, `tests/MSOSync.IntegrationTests/Transport/*.cs` | [task13](2026-06-22-epic6-task13-scheduler-integration-tests.md) |

---

## Dependency Chain

```
Task 1 (entities + enums)
  ↓
Task 2 (BatchStatus + state machine) ─── Task 3 (Transport DTOs + compression)
  ↓                                              ↓
Task 5 (IBatchTransportQueryService)    Task 4 (INodeHttpClient)
  ↓___________________________________________↓
Task 6 (SmartTransportService + AcknowledgementService)
  ↓
Task 7 (PushClient + PullClient)
  ↓
Task 8 (IApplyService + update ITransportService)
  ↓
Task 9 (SyncController)    Task 10 (ITopologyService + PullJob)
  ↓_____________________________↓
Task 11 (DI wiring + csproj)
  ↓
Task 12 (Unit tests) + Task 13 (SchedulerRecovery + Integration tests)
```

Tasks 3 and 4 are independent of each other and can proceed in parallel after Task 1.
Tasks 12 and 13 can proceed in parallel after Task 11.

---

## Architectural Notes for Implementers

**IBatchTransportQueryService placement:** The spec places this interface in `MSOSync.Transport`, but `MSOSync.Transport.csproj` already references `MSOSync.Batch`. Placing the interface in Transport AND having Batch implement it creates a circular project reference. Resolution: place `IBatchTransportQueryService` in `MSOSync.Batch` alongside `IBatchStateMachine`. Transport references Batch (already does) and uses both interfaces through that single reference. This satisfies the spec's intent (Transport is persistence-free) without the circularity.

**ITopologyService placement:** A `MSOSync.Topology` project already exists (placeholder) with description "TopologyService, TopologyCache". `ITopologyService` and `TopologyService` go there, not in Metadata. Returns `IReadOnlyList<SourceNodeInfo>` (simple record, no Metadata dep) instead of `IReadOnlyList<NodeDto>`.

**BatchStateMachine needs IClock:** `MoveToSendingAsync` must set `SentTime = clock.UtcNow`. Add `IClock clock` to constructor injection. Update `BatchPipelineExtensions` — IClock is already a singleton in DI.

---

## Verification (after Task 13)

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.TransportTests -c Debug
dotnet test tests/MSOSync.EngineTests -c Debug
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Transport" -c Debug
```

Expected: build clean, all tests green.
