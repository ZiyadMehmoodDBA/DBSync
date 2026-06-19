# Epic 4: Metadata — Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the full metadata management layer — CRUD services and REST API for nodes, triggers, routers, channels, and parameters. Remove plaintext `node_token` (TD002). Add `ICurrentUserService` and global exception handler.

**Architecture:** Interface-based service layer (`I*MetadataService` → `*MetadataService`) in `MSOSync.Metadata`, registered via `AddMetadata()`. Controllers in `MSOSync.Api` inject service interfaces. All reads `AsNoTracking()`. `IMemoryCache` with 60-second absolute expiration. MediatR publishes domain events; no listeners in this epic. Exception types in `MSOSync.Common`; `IExceptionHandler` in `MSOSync.Api` maps them to standard error envelope. DTOs always returned — EF entities never cross module boundaries.

**Tech Stack:** EF Core 9.0.0, MediatR 12.4.1, FluentValidation 11.11.0, Microsoft.Extensions.Caching.Memory 9.0.0, BCrypt.Net-Next 4.0.3, xUnit 2.9.3, Microsoft.EntityFrameworkCore.Sqlite 9.0.0, Testcontainers.MsSql 4.4.0, Moq 4.20.72, FluentAssertions 6.12.2

## Global Constraints

- .NET 9 / C# 13 / ASP.NET Core 9 / EF Core 9.0.0 / SQL Server
- All routes: `api/v1/`
- `TreatWarningsAsErrors = true` — zero warnings
- Never `git add .` or `git add -A` — stage by name only
- No EF InMemory in tests — use SQLite in-memory for unit tests, Testcontainers for integration
- DTOs always returned — no raw EF entities across boundaries
- Cache: `IMemoryCache`, absolute 60s, key format `metadata:{domain}:{id}`
- Exception types in `MSOSync.Common/Exceptions/`
- Secrets never logged or returned — mask as `*****` in DTOs and history rows
- dotnet PATH (BOTH required for all `dotnet` commands):
  ```powershell
  $env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

## Tasks

| # | File | Deliverable | Verify |
|---|------|-------------|--------|
| 1 | [task1-migration.md](2026-06-19-epic4-task1-migration.md) | M011 migration + `SyncNodeSecurity` entity cleanup + `NodeSecurityService.PrepareToken` | `dotnet build` |
| 2 | [task2-current-user.md](2026-06-19-epic4-task2-current-user.md) | `ICurrentUserService` + `HttpContextCurrentUserService` + `ClaimTypes.Name` in JWT | `dotnet test` |
| 3 | [task3-exceptions.md](2026-06-19-epic4-task3-exceptions.md) | Exception hierarchy + `GlobalExceptionHandler` | `dotnet build` |
| 4 | [task4-parameter-service.md](2026-06-19-epic4-task4-parameter-service.md) | `ParameterMetadataService` + `ParameterDescriptor` catalog + DTOs | `dotnet build` |
| 5 | [task5-node-service.md](2026-06-19-epic4-task5-node-service.md) | `NodeMetadataService` + node DTOs | `dotnet build` |
| 6 | [task6-trigger-service.md](2026-06-19-epic4-task6-trigger-service.md) | `TriggerMetadataService` + trigger DTOs | `dotnet build` |
| 7 | [task7-router-channel-service.md](2026-06-19-epic4-task7-router-channel-service.md) | `RouterMetadataService` + `ChannelMetadataService` + DTOs | `dotnet build` |
| 8 | [task8-wire.md](2026-06-19-epic4-task8-wire.md) | `AddMetadata()` extension + `Program.cs` wiring | `dotnet test` |
| 9 | [task9-controllers.md](2026-06-19-epic4-task9-controllers.md) | 6 controllers + validators + `AddTriggerRouterRequest` | `dotnet build` |
| 10 | [task10-unit-tests.md](2026-06-19-epic4-task10-unit-tests.md) | `MSOSync.MetadataTests` project — SQLite in-memory unit tests | `dotnet test` |
| 11 | [task11-integration-tests.md](2026-06-19-epic4-task11-integration-tests.md) | `MetadataFixture` + `MetadataTests` — Testcontainers integration tests | `dotnet test` |

---

## Key Interfaces (Cross-Task Reference)

```
NodeProvisionResult       : sealed record(string NodeId, string RawToken)                           [Task 1]
ICurrentUserService       : GetCurrentUsername() → string                                           [Task 2]
SyncException             : abstract, Code property                                                  [Task 3]
NotFoundException         : SyncException, code "NOT_FOUND"                                         [Task 3]
DuplicateEntityException  : SyncException, code "DUPLICATE_ENTITY"                                  [Task 3]
ValidationException       : SyncException, code "VALIDATION_ERROR"                                  [Task 3]
ForbiddenOperationException: SyncException, code "FORBIDDEN"                                        [Task 3]
ConcurrencyException      : SyncException, code "CONCURRENCY_CONFLICT"                              [Task 3]
UnauthorizedException     : SyncException, code "UNAUTHORIZED"                                      [Task 3]

IParameterMetadataService :                                                                         [Task 4]
  GetParametersAsync() → Task<IReadOnlyList<ParameterDto>>
  GetParameterAsync(name) → Task<ParameterDto?>
  UpdateParameterAsync(name, value) → Task
  GetParameterHistoryAsync(name) → Task<IReadOnlyList<ParameterHistoryDto>>
  GetAllParameterHistoryAsync() → Task<IReadOnlyList<ParameterHistoryDto>>

INodeMetadataService :                                                                              [Task 5]
  GetNodesAsync() → Task<IReadOnlyList<NodeDto>>
  GetNodeAsync(nodeId) → Task<NodeDto?>
  GetNodeGroupsAsync() → Task<IReadOnlyList<NodeGroupDto>>
  UpdateNodeAsync(nodeId, UpdateNodeRequest) → Task<NodeDto>
  EnableNodeAsync(nodeId) → Task
  DisableNodeAsync(nodeId) → Task
  GetPendingRegistrationsAsync() → Task<IReadOnlyList<RegistrationRequestDto>>
  ApproveRegistrationAsync(requestId) → Task<NodeProvisionResult>
  RejectRegistrationAsync(requestId) → Task
  GetNodeSecurityInfoAsync(nodeId) → Task<NodeSecurityInfoDto>

ITriggerMetadataService :                                                                           [Task 6]
  GetTriggersAsync() → Task<IReadOnlyList<TriggerDto>>
  GetTriggerAsync(triggerId) → Task<TriggerDto?>
  GetTriggersForChannelAsync(channelId) → Task<IReadOnlyList<TriggerDto>>
  CreateTriggerAsync(CreateTriggerRequest) → Task<TriggerDto>
  UpdateTriggerAsync(triggerId, UpdateTriggerRequest) → Task<TriggerDto>
  DeleteTriggerAsync(triggerId) → Task
  EnableTriggerAsync(triggerId) → Task
  DisableTriggerAsync(triggerId) → Task
  GetTriggerRoutersAsync(triggerId) → Task<IReadOnlyList<TriggerRouterDto>>
  AddTriggerRouterAsync(triggerId, routerId) → Task
  RemoveTriggerRouterAsync(triggerId, routerId) → Task
  GetTriggerHistoryAsync(triggerId) → Task<IReadOnlyList<TriggerHistDto>>

IRouterMetadataService :                                                                            [Task 7]
  GetRoutersAsync() → Task<IReadOnlyList<RouterDto>>
  GetRouterAsync(routerId) → Task<RouterDto?>
  GetRoutersForSourceGroupAsync(groupId) → Task<IReadOnlyList<RouterDto>>
  GetRoutersForTargetGroupAsync(groupId) → Task<IReadOnlyList<RouterDto>>
  CreateRouterAsync(CreateRouterRequest) → Task<RouterDto>
  UpdateRouterAsync(routerId, UpdateRouterRequest) → Task<RouterDto>
  DeleteRouterAsync(routerId) → Task

IChannelMetadataService :                                                                           [Task 7]
  GetChannelsAsync() → Task<IReadOnlyList<ChannelDto>>
  GetChannelAsync(channelId) → Task<ChannelDto?>
  CreateChannelAsync(CreateChannelRequest) → Task<ChannelDto>
  UpdateChannelAsync(channelId, UpdateChannelRequest) → Task<ChannelDto>
  DeleteChannelAsync(channelId) → Task
```

## SDD Progress Ledger

Track completed tasks in `D:\MSOSync\.git\sdd\progress-epic4.md`. Append one line per completed task:
```
Task N: complete (commits <base7>..<head7>, review clean)
```
