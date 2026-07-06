# Epic 12A: Node Registration & Provisioning UX — Design Spec

**Date:** 2026-07-06
**Status:** Approved

---

## 1. Overview

Node Management is a new first-class feature module in MSOSync CE that consolidates the full lifecycle of sync node participation: reviewing inbound registration requests, approving/rejecting them (with diff preview for re-registrations), provisioning new nodes with a guided wizard, and browsing existing nodes and groups.

The module lives at `/nodes` (or `/node-management`) and exposes its own backend controller under `/api/v1/node-management`. It is P1 in the 12A–12E roadmap.

**Scope (Epic 12A):**
- Registration queue: view, approve, reject (single + bulk)
- Re-registration diff preview
- Provisioning wizard (5-step, package download)
- Overview dashboard (stats summary)
- Node list + Group list (read-only browse, edit out of scope)

---

## 2. Architecture

### 2.1 Route & Navigation

```
/nodes  →  302 redirect  →  /node-management
/node-management  →  NodeManagementPage
```

`NodeManagementPage` renders a tab strip with 5 tabs:

| Tab | Route Segment | Permission |
|-----|---------------|------------|
| Overview | (default) | VIEW_TOPOLOGY |
| Registrations | registrations | VIEW_TOPOLOGY |
| Provision | provision | MANAGE_USERS |
| Nodes | nodes | VIEW_TOPOLOGY |
| Groups | groups | VIEW_TOPOLOGY |

Tabs are lazy-loaded via `React.lazy()`. Prefetch on hover. Unauthorized tabs are hidden (not shown as disabled).

### 2.2 Feature Folder Structure

```
src/MSOSync.Frontend/features/node-management/
  overview/
    components/OverviewTab.tsx
    components/StatCard.tsx
  registrations/
    components/RegistrationsTab.tsx
    components/RegistrationQueue.tsx
    components/RegistrationDetailPanel.tsx
    components/BulkActionToolbar.tsx
    components/DiffTable.tsx          ← wraps shared DiffViewer
  provision/
    components/ProvisionTab.tsx
    components/ProvisionWizard.tsx
    steps/
      Step1NodeType.tsx
      Step2Credentials.tsx
      Step3Network.tsx
      Step4Review.tsx
      Step5Complete.tsx
  nodes/
    components/NodesTab.tsx
  groups/
    components/GroupsTab.tsx
  shared/
    components/
      DiffViewer.tsx                  ← reusable across future epics
  types/
    tabs.ts                           ← NODE_MANAGEMENT_TABS constant + TabId type
    registration.ts
    provision.ts
  api/
    nodeManagementApi.ts
  hooks/
    useNodeManagementRegistrations.ts
    useApproveRegistration.ts
    useRejectRegistration.ts
    useBulkApproveRegistrations.ts
    useBulkRejectRegistrations.ts
    useProvisionPackage.ts
    useNodeManagementOverview.ts
    useRegistrationDetail.ts
  NodeManagementPage.tsx
  NodeManagementProvider.tsx
```

### 2.3 Backend Module Structure

```
src/MSOSync.Metadata/NodeManagement/
  RegistrationMetadataDto.cs
  RegistrationDiffDto.cs
  RegistrationDiffService.cs
  INodeLifecycleService.cs          ← orchestrator interface (NEW)
  NodeLifecycleService.cs           ← orchestrator impl (NEW)
  INodeManagementService.cs
  NodeManagementService.cs
  ProvisionPackageService.cs
  NodeManagementDtos.cs
src/MSOSync.App/Controllers/
  NodeManagementController.cs
src/MSOSync.Persistence/Migrations/
  M020_AddRegistrationMetadata.cs
```

---

## 3. Database

### 3.1 Migration M020

```sql
ALTER TABLE sync_registration_request
  ADD metadata_json     nvarchar(max)  NULL,
      registration_type nvarchar(20)   NOT NULL DEFAULT 'New';
```

EF Core migration class: `M020_AddRegistrationMetadata`.

### 3.2 Entity Changes

`SyncRegistrationRequest` gains four properties:
- `string? MetadataJson` — structured JSON, nullable for legacy rows
- `RegistrationType RegistrationType` — enum property; EF Core converts to/from `nvarchar(20)` string column via `HasConversion<string>()`
- `RegistrationStatus Status` — enum property; EF Core converts to/from `nvarchar(20)` string column via `HasConversion<string>()`
- `byte[] RowVersion` — SQL Server `rowversion` column for optimistic concurrency; mapped with `IsRowVersion()`

All three enum properties use string converters, not raw `string` fields — eliminates string comparisons throughout the backend while preserving the existing column type.

---

## 4. Domain Model

### 4.1 RegistrationType Enum

```csharp
public enum RegistrationType { New, ReRegistration, Recovery }
```

Serialized as string in API responses (`"New"`, `"ReRegistration"`, `"Recovery"`).

Backend derives the value when a registration request is received:
- No existing `SyncNode` with matching `ExternalId` → `New`
- Existing node, status `Active` → `ReRegistration`
- Existing node, status `Offline`/`Error` → `Recovery`

### 4.2 RegistrationStatus Enum

```csharp
public enum RegistrationStatus { Pending, Approved, Rejected }
```

Serialized as string (`"Pending"`, `"Approved"`, `"Rejected"`). Replaces all literal string comparisons in service/query code.

### 4.3 RegistrationMetadataDto

```csharp
public sealed record RegistrationMetadataDto(
    int SchemaVersion,                    // required at root, current = 1
    MachineMetadata?     Machine,
    DatabaseMetadata?    Database,
    ApplicationMetadata? Application,
    HardwareMetadata?    Hardware
);

public sealed record MachineMetadata(
    string? HostName,
    string? OsVersion,
    string? MachineName
);

public sealed record DatabaseMetadata(
    string? Edition,
    string? Version,
    string? Collation,
    string? InstanceName
);

public sealed record ApplicationMetadata(
    string? AgentVersion,
    string? RuntimeVersion,
    string? InstallPath
);

public sealed record HardwareMetadata(
    int?    CpuCount,
    long?   RamBytes,
    long?   DiskBytes
);
```

Validation on inbound POST: deserialize → validate `SchemaVersion >= 1` → persist. Unknown fields silently ignored. Missing sub-records allowed (all nullable).

### 4.4 RegistrationDiffDto

```csharp
public sealed record RegistrationDiffDto(
    IReadOnlyList<RegistrationDiffItemDto> Items
);

public sealed record RegistrationDiffItemDto(
    string              Field,
    string?             CurrentValue,
    string?             IncomingValue,
    RegistrationChangeType ChangeType
);

public enum RegistrationChangeType { Unchanged, Added, Modified, Removed }
```

`RegistrationDiffService` compares flattened fields from the inbound `RegistrationMetadataDto` against the corresponding live `SyncNode` properties. Only `Modified`, `Added`, and `Removed` fields are returned by default (client can request `Unchanged` via query param).

---

## 5. Backend API

### 5.1 Controller: NodeManagementController

Base route: `/api/v1/node-management`

| Method | Path | Permission | Description |
|--------|------|-----------|-------------|
| GET | `/registrations` | VIEW_TOPOLOGY | Paginated registration queue |
| GET | `/registrations/{id}` | VIEW_TOPOLOGY | Detail + diff preview |
| POST | `/registrations` | (internal/agent — no UI auth required for inbound) | Receive new registration |
| POST | `/registrations/{id}/approve` | APPROVE_NODES | Approve single |
| POST | `/registrations/{id}/reject` | APPROVE_NODES | Reject single |
| POST | `/registrations/bulk-approve` | APPROVE_NODES | Bulk approve, 207 |
| POST | `/registrations/bulk-reject` | APPROVE_NODES | Bulk reject, 207 |
| GET | `/overview` | VIEW_TOPOLOGY | Stats summary |
| POST | `/provision` | MANAGE_USERS | Create provisioned node, return token once |
| POST | `/provision-package` | MANAGE_USERS | Generate & stream ZIP package |

### 5.2 Registration List

```
GET /api/v1/node-management/registrations
  ?status=Pending|Approved|Rejected          (optional filter)
  ?registrationType=New|ReRegistration|Recovery (optional filter)
  ?pageSize=int                              (default 50, max 500)
  ?cursor=string                             (cursor pagination)
  &includeTotalCount=true

Response: CursorPageResult<RegistrationSummaryDto>
```

```csharp
public sealed record RegistrationSummaryDto(
    long               Id,
    string             NodeExternalId,
    string             NodeName,
    RegistrationType   RegistrationType,
    RegistrationStatus Status,
    DateTime           ReceivedAt,
    DateTime?          ProcessedAt,
    string?            ProcessedBy
);
```

### 5.3 Registration Detail

```
GET /api/v1/node-management/registrations/{id}

Response: RegistrationDetailDto
```

```csharp
public sealed record RegistrationDetailDto(
    long                     Id,
    string                   NodeExternalId,
    string                   NodeName,
    RegistrationType         RegistrationType,
    RegistrationStatus       Status,
    DateTime                 ReceivedAt,
    DateTime?                ProcessedAt,
    string?                  ProcessedBy,
    RegistrationMetadataDto? Metadata,
    RegistrationDiffDto?     Diff            // null for New registrations
);
```

### 5.4 Approve / Reject

**Single approve:**
```
POST /api/v1/node-management/registrations/{id}/approve
Body: { "notes": "string?" }
Response: 204 No Content
```

**Single reject:**
```
POST /api/v1/node-management/registrations/{id}/reject
Body: { "reason": "string?" }
Response: 204 No Content
```

Reject = status change to `Rejected` + audit write. Request record is retained (immutable history).

**Bulk approve:**
```
POST /api/v1/node-management/registrations/bulk-approve
Body: { "ids": [long] }
Response: 207 Multi-Status
[
  { "id": 1, "status": "Approved" },
  { "id": 2, "status": "AlreadyApproved" },   // idempotent skip
  { "id": 3, "status": "NotFound" }
]
```

**Bulk reject:**
```
POST /api/v1/node-management/registrations/bulk-reject
Body: { "ids": [long], "reason": "string?" }
Response: 207 Multi-Status   (same per-item shape)
```

### 5.5 Overview

```
GET /api/v1/node-management/overview

Response: NodeManagementOverviewDto
```

```csharp
public sealed record NodeManagementOverviewDto(
    int       PendingRegistrations,
    int       PendingRecoveries,          // subset of PendingRegistrations where RegistrationType == Recovery
    int       TotalNodes,
    int       ActiveNodes,
    int       OfflineNodes,
    int       DegradedNodes,
    int       TotalGroups,
    DateTime? LastRegistrationAt,         // UTC timestamp of most recent inbound registration
    DateTime? LastApprovalAt,             // UTC timestamp of most recent approval
    DateTime  GeneratedAt
);
```

### 5.6 Provision (wizard completion)

```
POST /api/v1/node-management/provision
Body: ProvisionRequestDto

Response: 201 Created
{ "nodeId": "string", "token": "string" }
```

```csharp
public sealed record ProvisionRequestDto(
    string  NodeName,
    string  ExternalId,
    string  NodeType,        // "source" | "target"
    string  DbServer,
    string  DbName,
    string? GroupId,
    string? Description
);
```

**Token security:**
- Token returned exactly once in the 201 response body
- Never logged (audit writes only `"token:issued"` — no token value)
- Never published via SignalR
- UI displays one-time warning on Step5 Complete screen; if user refreshes, shows "token cannot be recovered — you must re-provision"

### 5.7 Provision Package

```
POST /api/v1/node-management/provision-package
Body: { "nodeId": "string", "token": "string" }

Response: 200 application/zip
Content-Disposition: attachment; filename="msosync-node-{nodeId}.zip"
```

Package contents:

| File | Description |
|------|-------------|
| `msosync-node.json` | Node config: id, externalId, name, type, groupId, serverUrl, created |
| `.env.example` | Template env vars with placeholders (no actual secrets) |
| `README.md` | Setup instructions |
| `manifest.json` | Package metadata: nodeId, agentVersion, generatedAt, fileCount |
| `checksums.txt` | SHA-256 hash per file, one line each |

**Streaming:** `ProvisionPackageService` writes directly to `HttpResponse.Body` via a `ZipArchive` opened over the response stream — no intermediate `MemoryStream`. `Content-Length` header is omitted (chunked transfer). This scales to larger packages in future epics without buffering.

Audit action `PROVISION_PACKAGE_DOWNLOADED` written on every download.

### 5.8 Inbound Registration (agent-facing)

```
POST /api/v1/node-management/registrations
Body: InboundRegistrationDto
  {
    "externalId": "string",
    "nodeName": "string",
    "nodeType": "string",
    "metadata": { ...RegistrationMetadataDto... }
  }

Response: 202 Accepted
{ "registrationId": long }
```

Validation pipeline:
1. Model validation (FluentValidation)
2. Deserialize + validate `metadata.SchemaVersion >= 1`
3. Derive `RegistrationType` from existing node lookup
4. Persist `SyncRegistrationRequest` with serialized `MetadataJson`
5. Publish MediatR notification (no handler in 12A — reserved for future SignalR push)
6. Audit action `NODE_REGISTERED`

### 5.9 INodeLifecycleService

Thin orchestrator — controller calls this, not individual services directly.

```csharp
public interface INodeLifecycleService
{
    Task<long>   RegisterAsync(InboundRegistrationDto dto, CancellationToken ct);
    Task         ApproveAsync(long id, string? notes, string actorUsername, CancellationToken ct);
    Task         RejectAsync(long id, string? reason, string actorUsername, CancellationToken ct);
    Task<IReadOnlyList<BulkResultItemDto>> BulkApproveAsync(IReadOnlyList<long> ids, string actorUsername, CancellationToken ct);
    Task<IReadOnlyList<BulkResultItemDto>> BulkRejectAsync(IReadOnlyList<long> ids, string? reason, string actorUsername, CancellationToken ct);
    Task<ProvisionResultDto> ProvisionAsync(ProvisionRequestDto dto, string actorUsername, CancellationToken ct);
}
```

Collaborators: `IRegistrationDiffService`, `ProvisionPackageService`, `IAuditService`, metrics.

### 5.10 RegistrationDiffService

Standalone service — not embedded in `INodeLifecycleService`.

```csharp
public interface IRegistrationDiffService
{
    RegistrationDiffDto Compute(
        RegistrationMetadataDto incoming,
        SyncNode               currentNode,
        bool                   includeUnchanged = false);
}
```

Used in two places:
1. `GET /registrations/{id}` — preview (read-only)
2. `INodeLifecycleService.ApproveAsync` — audit write (logs changed fields)

---

## 6. Audit Actions

| Action Constant | Trigger |
|----------------|---------|
| `NODE_REGISTERED` | Inbound registration received |
| `NODE_APPROVED` | Single or bulk approve |
| `NODE_REJECTED` | Single or bulk reject |
| `NODE_RE_REGISTERED` | Approved registration where `RegistrationType == ReRegistration` |
| `PROVISION_PACKAGE_DOWNLOADED` | Every ZIP package download |

---

## 7. Permissions

| Permission | Covers |
|-----------|--------|
| `VIEW_TOPOLOGY` | Read all tabs (overview, registrations list/detail, nodes, groups) |
| `APPROVE_NODES` | Approve/reject single and bulk |
| `MANAGE_USERS` | Provision wizard + package download |

---

## 8. Frontend Design

### 8.1 Tab Constants

```typescript
// features/node-management/types/tabs.ts
export const NODE_MANAGEMENT_TABS = {
  OVERVIEW:      'overview',
  REGISTRATIONS: 'registrations',
  PROVISION:     'provision',
  NODES:         'nodes',
  GROUPS:        'groups',
} as const;

export type TabId = (typeof NODE_MANAGEMENT_TABS)[keyof typeof NODE_MANAGEMENT_TABS];
```

### 8.2 NodeManagementProvider

React context owning shared UI state across tabs:

```typescript
interface NodeManagementContextValue {
  activeTab:          TabId;
  setActiveTab:       (tab: TabId) => void;
  selectedRegistration: RegistrationSummaryDto | null;
  setSelectedRegistration: (r: RegistrationSummaryDto | null) => void;
  bulkSelection:      Set<long>;
  toggleBulkSelect:   (id: long) => void;
  clearBulkSelection: () => void;
  wizardDraft:        ProvisionWizardDraft | null;
  setWizardDraft:     (d: ProvisionWizardDraft | null) => void;
}
```

Provider wraps `NodeManagementPage`. Tabs communicate through context rather than prop-drilling.

### 8.3 Data Fetching

Each tab uses independent TanStack Query queries (not shared query). Query keys:

```typescript
nodeManagementKeys = {
  overview:          () => ['node-management', 'overview'],
  registrations:     (filters) => ['node-management', 'registrations', filters],
  registrationDetail:(id) => ['node-management', 'registrations', id],
  nodes:             (filters) => ['node-management', 'nodes', filters],
  groups:            () => ['node-management', 'groups'],
}
```

Prefetch registration detail on registration row hover (via `queryClient.prefetchQuery`).

### 8.4 Registrations Tab

- Split-pane layout: queue list (left) + detail panel (right, slides in on row select)
- Sticky bulk action toolbar appears when `bulkSelection.size > 0`
- Bulk toolbar: "Approve N" + "Reject N" + "Clear" buttons
- Filter bar: Status, RegistrationType dropdowns
- `DiffTable` renders `RegistrationDiffDto.items` in a 4-column table: Field / Current / Incoming / Change (colored badge)

### 8.5 DiffViewer (Shared)

```typescript
// features/node-management/shared/components/DiffViewer.tsx
interface DiffViewerProps {
  items: RegistrationDiffItemDto[];
  defaultView?: 'changes' | 'all';   // default: 'changes'
}
```

Renders a 4-column table (Field / Current / Incoming / Change) with:
- `Modified` → yellow/amber row
- `Added` → green row (current empty, incoming has value)
- `Removed` → red row (current has value, incoming empty)
- `Unchanged` → gray, shown only in `'all'` view

Toggle button in the component header: **"Only Changed"** ↔ **"Show All"** — toggling remembers the choice in local state. Operators can switch repeatedly during review without losing their scroll position.

Reusable across future epics (e.g., Epic 12B node config comparison).

### 8.6 Provisioning Wizard

5-step wizard, state persisted to `sessionStorage` under key `"msosync:wizard:provision"`:

| Step | Content |
|------|---------|
| Step 1 — Node Type | radio: "Hub (source)", "Leaf (target)", optional description |
| Step 2 — Credentials | DB Server, DB Name, optionally test connection |
| Step 3 — Network | Node name, External ID (auto-generated from name, editable), Group assignment |
| Step 4 — Review | Summary of all entries, read-only |
| Step 5 — Complete | Token display (one-time), download package button, warning banner |

**Step 5 token behavior:**
- Token displayed in a masked field with "reveal" toggle and copy button
- Warning: "This token will not be shown again. If you navigate away before saving it, you must re-provision this node."
- If user refreshes Step 5 (token gone from state): show "Token cannot be recovered — return to Step 1 to provision again"
- Token never sent to any SignalR channel; never written to any log

**sessionStorage resilience:**

Draft stored under key `"msosync:wizard:provision"` as a versioned envelope:

```json
{ "version": 1, "draft": { "step": 2, "nodeType": "target", ... } }
```

- `version` is the wizard schema version (increment in Epic 12B+ if wizard fields change)
- On mount: read envelope; if `version` does not match current, discard silently and start fresh; if `version` matches and `draft` exists, offer "Resume draft?" toast
- Draft saved on every wizard step advance
- Draft cleared on: Step 5 completion (success), explicit Cancel, or navigating away from Provision tab

### 8.7 Hooks

```typescript
// Read
useNodeManagementOverview()       → NodeManagementOverviewDto
useNodeManagementRegistrations(filters) → CursorPageResult<RegistrationSummaryDto>
useRegistrationDetail(id)         → RegistrationDetailDto

// Mutations
useApproveRegistration()          → mutate({ id, notes? })
useRejectRegistration()           → mutate({ id, reason? })
useBulkApproveRegistrations()     → mutate({ ids })
useBulkRejectRegistrations()      → mutate({ ids, reason? })
useProvisionPackage()             → mutate({ nodeId, token }) → triggers file download
```

After approve/reject mutations: invalidate `registrations` + `overview` query keys.

---

## 9. Error Handling

| Scenario | Backend | Frontend |
|---------|---------|----------|
| Invalid `MetadataJson` | 400 with field-level errors | Form inline error |
| Registration not found | 404 | Toast error |
| Approve already-approved (single) | 409 Conflict | Toast "Already approved" |
| Approve already-approved (bulk) | 207 per-item `"AlreadyApproved"` | Summary toast |
| Unauthorized action | 403 | Tab hidden; toast if direct URL access |
| Provision package > 5 MB | 500 (should not occur — ZIP in-memory) | Generic toast |
| Token refresh on Step 5 | N/A | "Cannot be recovered" inline message |

---

## 10. Testing

### 10.1 Unit Tests (`MSOSync.MetadataTests`)

- `RegistrationDiffService`: all `RegistrationChangeType` permutations
- `ProvisionPackageService`: ZIP contains exactly 5 files, checksums valid
- `NodeManagementService`: approve/reject state transitions, idempotent bulk
- `RegistrationMetadataDto` deserialization: valid JSON, missing sub-records, unknown fields, `SchemaVersion` validation

### 10.2 Integration Tests (`MSOSync.IntegrationTests/NodeManagement/`)

Standard fixture (Testcontainers MsSql 4.4.0):

- **Happy path:** register → queue appears → approve → node activated
- **Re-registration diff:** second registration for same externalId → diff computed
- **Bulk approve/reject:** 3 registrations → bulk approve 2 + 1 already-approved → 207 with mixed statuses
- **Provision:** POST provision → 201 with token → POST provision-package → 200 zip
- **Authorization:** VIEW_TOPOLOGY cannot approve; APPROVE_NODES cannot provision; unauthenticated → 401
- **Concurrency:** two concurrent approvals for same registration → one 204, one 409

### 10.3 Metrics

| Metric | Type | Labels |
|--------|------|--------|
| `msosync_registration_requests_total` | Counter | `type`, `status` |
| `msosync_registrations_approved_total` | Counter | — |
| `msosync_registrations_rejected_total` | Counter | — |
| `msosync_provision_packages_downloaded_total` | Counter | — |
| `msosync_registration_duration_seconds` | Histogram | `type` |
| `msosync_bulk_registration_duration_seconds` | Histogram | `operation` (`approve`/`reject`) |
| `msosync_provision_package_generation_seconds` | Histogram | — |

Histograms expose `_sum`, `_count`, and `_bucket` automatically via `prometheus-net`. They are more actionable than counters alone: a spike in `_sum / _count` reveals slow approvals before they become user-visible.

---

## 11. Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`
- EF Core 9.0.0 — migration class name `M020_AddRegistrationMetadata`
- Cursor pagination: `CursorPageResult<T>` (no `PagedResult<T>`)
- FluentValidation 11.11.0
- MediatR 12.4.1 — publish `RegistrationReceivedNotification` (no handler in 12A)
- xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72
- Testcontainers.MsSql 4.4.0 for integration tests
- React 19, TanStack Query v5
- No new npm packages
- Permissions stored as `SystemPermissions` string constants: `VIEW_TOPOLOGY`, `APPROVE_NODES`, `MANAGE_USERS`
- `RegistrationType` serialized as PascalCase string (`"New"`, `"ReRegistration"`, `"Recovery"`)
- `RegistrationStatus` serialized as PascalCase string (`"Pending"`, `"Approved"`, `"Rejected"`)
- `RegistrationChangeType` serialized as PascalCase string
- All three enums stored as `nvarchar(20)` via EF Core `HasConversion<string>()` — no raw string properties on entity
- `SyncRegistrationRequest.RowVersion` → `byte[]`, mapped with `IsRowVersion()` in EF config
- `INodeLifecycleService` is the single orchestration point for the controller — never call `IRegistrationDiffService` or `ProvisionPackageService` directly from the controller
- ZIP streamed directly to `HttpResponse.Body` — no intermediate `MemoryStream`; omit `Content-Length` header
- Wizard draft envelope: `{ "version": 1, "draft": {...} }` where current schema version = 1
- ZIP MIME type: `application/zip`
- Token: 32-byte cryptographically random, base64url encoded, returned once in 201 body only

---

## 12. Implementation Sequencing

Tasks execute in this order. Backend and frontend are not mixed within a task.

| Task | Scope | Deliverable |
|------|-------|-------------|
| 1 | Database + DTOs + Services | M020 migration, enums, entity, `RegistrationDiffService`, `INodeLifecycleService` + impl, unit tests |
| 2 | Registration APIs | `NodeManagementController` registration endpoints (list, detail, inbound POST, approve/reject, bulk), validators, integration tests |
| 3 | Overview + Provision APIs | overview endpoint, provision endpoint, `ProvisionPackageService` streaming, integration tests |
| 4 | Frontend shell + routing | Feature folder scaffold, `NodeManagementPage`, `NodeManagementProvider`, `NODE_MANAGEMENT_TABS`, lazy tabs, sidebar wiring |
| 5 | Registration queue | `RegistrationsTab`, `RegistrationQueue`, `RegistrationDetailPanel`, `DiffViewer`, `BulkActionToolbar`, hooks, query keys |
| 6 | Provision wizard | `ProvisionTab`, `ProvisionWizard` (5 steps), sessionStorage draft with versioning, `useProvisionPackage` |
| 7 | Testing + cleanup | Complete integration test suite, authorization tests, concurrency tests, metrics wiring, build clean |

---

## 13. Out of Scope (12B+)

- Node config editing (12B)
- Group management CRUD (12B)
- Real-time registration push via SignalR (future)
- Automated re-registration approval policies (future)
- Node decommission / removal workflow (future)
