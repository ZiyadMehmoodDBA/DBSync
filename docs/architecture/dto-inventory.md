# DTO Inventory

## Placement Convention

| Type | Location |
|---|---|
| Domain data transfer objects (query results, read models) | `src/MSOSync.Metadata/<Feature>/` or `src/MSOSync.Metadata/Dtos/` |
| API-specific request types | `src/MSOSync.Api/Dtos/<Feature>/` |
| API-specific response types | `src/MSOSync.Api/Dtos/<Feature>/` |

## Rules

- **RULE-DTO-1:** No DTOs defined inside controller files. All DTOs in `MSOSync.Api/Dtos/` or `MSOSync.Metadata/*/`.
- **RULE-DTO-2:** No duplicate DTO definitions for the same API resource. One canonical location per DTO type.
- **RULE-DTO-3:** Domain DTOs live in `MSOSync.Metadata`. API-specific request/response wrappers live in `MSOSync.Api/Dtos/`.

## API DTOs (`src/MSOSync.Api/Dtos/`)

| DTO | Namespace | Location |
|---|---|---|
| `LoginRequest` | `MSOSync.Api.Dtos.Auth` | `src/MSOSync.Api/Dtos/Auth/LoginRequest.cs` |
| `LoginResponse` | `MSOSync.Api.Dtos.Auth` | `src/MSOSync.Api/Dtos/Auth/LoginResponse.cs` |
| `RefreshRequest` | `MSOSync.Api.Dtos.Auth` | `src/MSOSync.Api/Dtos/Auth/RefreshRequest.cs` |
| `RefreshResponse` | `MSOSync.Api.Dtos.Auth` | `src/MSOSync.Api/Dtos/Auth/RefreshResponse.cs` |
| `MeResponse` | `MSOSync.Api.Dtos.Auth` | `src/MSOSync.Api/Dtos/Auth/MeResponse.cs` |
| `SwitchTenantRequest` | `MSOSync.Api.Dtos.Auth` | `src/MSOSync.Api/Dtos/Auth/SwitchTenantRequest.cs` |
| `BatchListRequest` | `MSOSync.Api.Dtos.Batches` | `src/MSOSync.Api/Dtos/Batches/BatchListRequest.cs` |
| `RetryAllResponse` | `MSOSync.Api.Dtos.Batches` | `src/MSOSync.Api/Dtos/Batches/RetryAllResponse.cs` |
| `CreateExportJobRequest` | `MSOSync.Api.Dtos.Export` | `src/MSOSync.Api/Dtos/Export/CreateExportJobRequest.cs` |
| `ExportJobDto` | `MSOSync.Api.Dtos.Export` | `src/MSOSync.Api/Dtos/Export/ExportJobDto.cs` |
| `AddTriggerRouterRequest` | `MSOSync.Api.Dtos.Nodes` | `src/MSOSync.Api/Dtos/Nodes/AddTriggerRouterRequest.cs` |
| `PatchNotificationRequest` | `MSOSync.Api.Dtos.Notifications` | `src/MSOSync.Api/Dtos/Notifications/PatchNotificationRequest.cs` |
| `PluginDto` | `MSOSync.Api.Dtos.Plugins` | `src/MSOSync.Api/Dtos/Plugins/PluginDto.cs` |
| `PluginSummaryDto` | `MSOSync.Api.Dtos.Plugins` | `src/MSOSync.Api/Dtos/Plugins/PluginSummaryDto.cs` |
| `PluginActionResult` | `MSOSync.Api.Dtos.Plugins` | `src/MSOSync.Api/Dtos/Plugins/PluginActionResult.cs` |

## Notable Domain DTOs

| DTO | Namespace | Location |
|---|---|---|
| `NodeDto` | `MSOSync.Metadata.Dtos` | `src/MSOSync.Metadata/Dtos/NodeDto.cs` |
| `HeartbeatRequest` | `MSOSync.Metadata.Dtos` | `src/MSOSync.Metadata/Dtos/HeartbeatRequest.cs` |
| `WorkerStatusDto` | `MSOSync.App.Workers` | `src/MSOSync.App/Workers/WorkerStatusDto.cs` |

*Note: This table lists notable DTOs. Full domain inventory lives under `src/MSOSync.Metadata/Dtos/` and per-feature folders in `MSOSync.Metadata`.*

## Verification

RULE-DTO-1 compliance check (expect controller classes only, no records/DTO classes):

```
grep -rn "^public sealed record\|^public record\|^public sealed class\|^public class" src/MSOSync.Api/Controllers/ --include="*.cs"
```

Last verified clean: 2026-07-21 (Phase 2A.6, findings 2A-003 and 2A-022).
