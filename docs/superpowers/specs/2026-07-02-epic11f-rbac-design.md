# Epic 11F: Fine-Grained RBAC Design

**Date:** 2026-07-02  
**Status:** Approved  
**Tech Stack:** C# 13 / .NET 9 / EF Core 9 / React 19 / TanStack Query v5 / TypeScript (erasableSyntaxOnly) / SignalR

---

## Goal

Extend MSOSync's three-role authorization model (ADMIN / OPERATOR / VIEWER) with a configurable per-role permission system. Administrators can grant and revoke specific capabilities per role from the frontend without SQL changes or server restarts. Permissions propagate to all connected clients in near real-time via SignalR.

---

## Architecture

**Phase 1 (this epic):** Permission-augmented roles. The three built-in roles remain. Two new tables (`sync_permission`, `sync_role_permission`) define which capabilities each role holds. A new API (`GET /api/v1/me/permissions`) returns the effective permissions for the current user. The frontend gates UI actions and routes on these permissions. Existing JWT claims and coarse-grained ASP.NET Core policies are unchanged.

**Phase 2 (future epic):** Custom roles. If enterprise deployments require roles beyond ADMIN / OPERATOR / VIEWER (e.g. "Export Only", "Audit Reader"), the `sync_role` + `sync_user_role` tables — which already exist — become the backing store for arbitrary named roles. The permission system built in Phase 1 extends naturally to those roles.

**Never:** Per-user permission overrides. User → Role → Permissions is the only supported model.

---

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- EF Core 9 — `AsNoTracking()` on all reads; `SaveChangesAsync(ct)` on writes
- No new NuGet packages; no new npm packages
- TypeScript `erasableSyntaxOnly = true` — no `enum`; use `as const`
- All frontend imports relative — no `@/` aliases
- Auth policy `"ViewerOrAbove"` on read endpoints; `"AdminOnly"` on role management endpoints
- Unit tests: `TestDbContext.Create()` (SQLite in-memory)
- Audit trail: every grant / revoke / reset / copy writes an entry to `SyncAudit`
- MANAGE_USERS cannot be revoked from ADMIN — fail with `PERMISSION_PROTECTED` 400
- Never `git add -A` or `git add .`

---

## Data Model

### New Tables (M018 migration)

#### `msosync.sync_permission`

```sql
permission_key   nvarchar(50)  PRIMARY KEY
display_name     nvarchar(100) NOT NULL
description      nvarchar(255) NULL
category         nvarchar(50)  NOT NULL   -- DATA | OPERATIONS | CONFIGURATION | ADMINISTRATION
sort_order       int           NOT NULL   DEFAULT 0
is_system        bit           NOT NULL   DEFAULT 1
```

#### `msosync.sync_role_permission`

```sql
role_name        nvarchar(50)  NOT NULL  FK → msosync.sync_role(role_name)
permission_key   nvarchar(50)  NOT NULL  FK → msosync.sync_permission(permission_key)
PRIMARY KEY (role_name, permission_key)
ON DELETE CASCADE (both FKs)
```

`role_name` references `SyncRole.RoleName` (the string column, not the surrogate key) — consistent with JWT role claims.

### Seed Data (M018)

#### Permissions Catalog

| permission_key | display_name | description | category | sort_order |
|---|---|---|---|---|
| VIEW_EVENTS | View Events | Access event list, filters, and details | DATA | 10 |
| VIEW_METRICS | View Metrics | Access dashboard metrics and charts | DATA | 20 |
| VIEW_AUDIT | View Audit | Access audit log and intelligence | DATA | 30 |
| VIEW_TOPOLOGY | View Topology | Access topology graph and node details | DATA | 40 |
| EXPORT_DATA | Export Data | Export events, batches, and audit records to CSV or JSON | DATA | 50 |
| RETRY_BATCHES | Retry Batches | Retry failed outgoing batches | OPERATIONS | 10 |
| APPROVE_NODES | Approve Nodes | Approve or reject node registration requests | OPERATIONS | 20 |
| RELEASE_LOCKS | Release Locks | Release active sync locks | OPERATIONS | 30 |
| EDIT_PARAMETERS | Edit Parameters | Modify sync parameter values | CONFIGURATION | 10 |
| MANAGE_TRIGGERS | Manage Triggers | Create, edit, enable, disable, and delete triggers and routers | CONFIGURATION | 20 |
| MANAGE_ROUTERS | Manage Routers | Create, edit, and delete routers and channels | CONFIGURATION | 30 |
| MANAGE_USERS | Manage Users | Create, edit, and delete user accounts | ADMINISTRATION | 10 |

#### Default Role Assignments

| Permission | VIEWER | OPERATOR | ADMIN |
|---|:---:|:---:|:---:|
| VIEW_EVENTS | ✓ | ✓ | ✓ |
| VIEW_METRICS | ✓ | ✓ | ✓ |
| VIEW_AUDIT | ✓ | ✓ | ✓ |
| VIEW_TOPOLOGY | ✓ | ✓ | ✓ |
| EXPORT_DATA | | ✓ | ✓ |
| RETRY_BATCHES | | ✓ | ✓ |
| APPROVE_NODES | | ✓ | ✓ |
| RELEASE_LOCKS | | ✓ | ✓ |
| EDIT_PARAMETERS | | | ✓ |
| MANAGE_TRIGGERS | | | ✓ |
| MANAGE_ROUTERS | | | ✓ |
| MANAGE_USERS | | | ✓ |

---

## Backend

### Service Interface

```csharp
// src/MSOSync.Metadata/Permissions/IPermissionService.cs
namespace MSOSync.Metadata.Permissions;

public interface IPermissionService
{
    Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken ct = default);
    Task<RolePermissionsDto> GetRolePermissionsAsync(string roleName, CancellationToken ct = default);
    Task<IReadOnlyList<RolePermissionsDto>> GetAllRolesAsync(CancellationToken ct = default);
    Task GrantPermissionAsync(string roleName, string permissionKey, CancellationToken ct = default);
    Task RevokePermissionAsync(string roleName, string permissionKey, CancellationToken ct = default);
    Task ResetRoleToDefaultsAsync(string roleName, CancellationToken ct = default);
    Task CopyPermissionsFromAsync(string targetRole, string sourceRole, CancellationToken ct = default);
}
```

### DTOs

```csharp
// EffectivePermissionsDto — returned by GET /me/permissions
public sealed record EffectivePermissionsDto(
    string Role,
    IReadOnlyList<string> Permissions,
    DateTimeOffset UpdatedAt);

// PermissionDto — catalog entry
public sealed record PermissionDto(
    string PermissionKey,
    string DisplayName,
    string? Description,
    string Category,
    int SortOrder,
    bool IsSystem);

// RolePermissionsDto — returned by GET /roles and GET /roles/{role}
public sealed record RolePermissionsDto(
    string RoleName,
    int UserCount,
    IReadOnlyList<PermissionDto> Permissions);
```

### Permission Resolution

`GetEffectivePermissionsAsync`:
1. Resolve username → `SyncUserRole` → `RoleName` (take first/primary role)
2. Look up `sync_role_permission` WHERE `role_name = RoleName`
3. Return `EffectivePermissionsDto` with `UpdatedAt` = max `updated_at` across relevant rows (or `DateTimeOffset.UtcNow` if cached)

### Caching

```
Read:   IMemoryCache key "permissions:{roleName}", 60s absolute expiration
Write:  Immediate cache eviction for affected roleName, then SignalR broadcast
```

Cache is an optimization only. Write-through invalidation ensures clients see changes promptly via SignalR even if another server node still has stale cache.

### Protection Rules

- `RevokePermissionAsync("ADMIN", "MANAGE_USERS")` → throws `ValidationException("PERMISSION_PROTECTED", "MANAGE_USERS cannot be revoked from ADMIN.")`
- `ResetRoleToDefaultsAsync("ADMIN")` → seed defaults always include MANAGE_USERS; this is safe

### Audit Trail

Every write operation calls `ISyncAuditService.WriteAsync` with:
- Resource: `"roles/{roleName}"`
- ActionName: `"GRANT_PERMISSION"` / `"REVOKE_PERMISSION"` / `"RESET_ROLE"` / `"COPY_PERMISSIONS"`
- ObjectName: `"{permissionKey}"` (or `"from:{sourceRole}"` for copy)

### SignalR Event

```csharp
public sealed record PermissionChangedEvent(
    string RoleName,
    string Action,      // "Grant" | "Revoke" | "Reset" | "Copy"
    DateTimeOffset OccurredAt);
```

Published via existing `ISyncEventPublisher` after every write. Clients subscribe and call `queryClient.invalidateQueries(queryKeys.permissions())` on receive.

### Controller

`PermissionsController` at `api/v1/`:

| Method | Route | Policy | Description |
|---|---|---|---|
| GET | `/me/permissions` | ViewerOrAbove | Effective permissions for current user |
| GET | `/permissions` | ViewerOrAbove | Full permissions catalog |
| GET | `/roles` | AdminOnly | All roles with permission sets and user counts |
| GET | `/roles/{role}` | AdminOnly | Role detail + permissions + affected users |
| PUT | `/roles/{role}/permissions/{key}` | AdminOnly | Grant permission to role |
| DELETE | `/roles/{role}/permissions/{key}` | AdminOnly | Revoke permission from role |
| POST | `/roles/{role}/reset` | AdminOnly | Reset role to M018 seed defaults |
| POST | `/roles/{role}/copy-from/{sourceRole}` | AdminOnly | Replace role permissions with copy of source role |

`copy-from` is idempotent: completely replaces target permissions with source, no merging.

---

## Frontend

### File Structure

**New files:**
```
src/shared/types/permissions.ts
src/shared/api/permissions.ts
src/shared/hooks/usePermissions.ts
src/shared/hooks/useRoles.ts
src/features/administration/RolesPage.tsx
src/features/administration/components/RolePermissionsCard.tsx
src/features/administration/components/CopyFromDialog.tsx
src/features/administration/components/ResetRoleDialog.tsx
```

**Modified files:**
```
src/shared/types/index.ts                   — export permissions types
src/shared/queryKeys.ts                     — add permissions, permissionCatalog, roles, role(name) keys
src/app/layouts/AppLayout.tsx               — prefetch usePermissions()
src/app/router.tsx                          — route guards for /administration/roles, /audit, /topology
src/features/audit/AuditPage.tsx            — gate on VIEW_AUDIT
src/features/topology/TopologyPage.tsx      — gate on VIEW_TOPOLOGY
src/features/events/EventsPage.tsx          — gate EXPORT_DATA on ExportMenu
src/features/incoming-batches/...           — gate EXPORT_DATA, RETRY_BATCHES
src/features/outgoing-batches/...           — gate EXPORT_DATA, RETRY_BATCHES
src/features/nodes/NodesPage.tsx            — gate APPROVE_NODES
src/features/locks/LocksPage.tsx            — gate RELEASE_LOCKS
src/features/parameters/ParametersPage.tsx  — gate EDIT_PARAMETERS
src/features/users/UsersPage.tsx            — add effective permissions column
src/shared/components/ExportMenu.tsx        — hide when !EXPORT_DATA
src/features/signalr/eventRouter.ts         — handle PermissionChangedEvent
```

### Hooks

```typescript
// usePermissions.ts
export function usePermissions(): UseQueryResult<EffectivePermissionsDto>
export function useHasPermission(key: string): boolean   // false while loading (fail-closed)

// useRoles.ts
export function usePermissionCatalog(): UseQueryResult<PermissionDto[]>
export function useRoles(): UseQueryResult<RolePermissionsDto[]>
export function useGrantPermission(): UseMutationResult<...>
export function useRevokePermission(): UseMutationResult<...>
export function useResetRole(): UseMutationResult<...>
export function useCopyFrom(): UseMutationResult<...>
```

`useHasPermission` returns `false` (not undefined, not null) when the query has not yet resolved. This enforces fail-closed behavior: protected UI never renders before permissions are known.

Grant/revoke mutations:
1. Optimistically update `queryKeys.role(roleName)` cache
2. On success: invalidate `queryKeys.roles()` and `queryKeys.permissions()`
3. On error: roll back optimistic update

### Permission Keys

```typescript
export const PermissionKeys = {
  VIEW_EVENTS:      'VIEW_EVENTS',
  VIEW_METRICS:     'VIEW_METRICS',
  VIEW_AUDIT:       'VIEW_AUDIT',
  VIEW_TOPOLOGY:    'VIEW_TOPOLOGY',
  EXPORT_DATA:      'EXPORT_DATA',
  RETRY_BATCHES:    'RETRY_BATCHES',
  APPROVE_NODES:    'APPROVE_NODES',
  RELEASE_LOCKS:    'RELEASE_LOCKS',
  EDIT_PARAMETERS:  'EDIT_PARAMETERS',
  MANAGE_TRIGGERS:  'MANAGE_TRIGGERS',
  MANAGE_ROUTERS:   'MANAGE_ROUTERS',
  MANAGE_USERS:     'MANAGE_USERS',
} as const;
export type PermissionKey = typeof PermissionKeys[keyof typeof PermissionKeys];
```

### UI Gating Rules

**Hide (show only when permission granted):**
- Sidebar navigation items: Events (VIEW_EVENTS), Audit (VIEW_AUDIT), Topology (VIEW_TOPOLOGY), Administration menu (MANAGE_USERS)
- ExportMenu on all pages: EXPORT_DATA
- Administration → Roles page (route + menu item): MANAGE_USERS

**Disable with tooltip (never hide — keep action visible):**
- Retry Batch / Retry All buttons: RETRY_BATCHES — disabled + "Requires RETRY_BATCHES permission"
- Approve / Reject node: APPROVE_NODES — disabled + "Requires APPROVE_NODES permission"
- Release Lock button: RELEASE_LOCKS — disabled + "Requires RELEASE_LOCKS permission"
- Edit Parameter form submit: EDIT_PARAMETERS — disabled + "Requires EDIT_PARAMETERS permission"
- Trigger / Router CRUD buttons: MANAGE_TRIGGERS / MANAGE_ROUTERS

**Route guards:**
- `/audit` → VIEW_AUDIT required; else render `<PermissionDeniedPage />`
- `/topology` → VIEW_TOPOLOGY required; else render `<PermissionDeniedPage />`
- `/administration/roles` → MANAGE_USERS required; else render `<PermissionDeniedPage />`

`<PermissionDeniedPage />` is a standalone 403 page — not a redirect.

### AppLayout

Add alongside the existing `usePreferences()` prefetch:

```tsx
usePermissions(); // prefetch — permissions available before any child route renders
```

### Admin Roles Page (`/administration/roles`)

Three role cards laid out in a responsive grid. Each card shows:
- Role name badge
- User count
- Permissions grouped by category (DATA / OPERATIONS / CONFIGURATION / ADMINISTRATION)
- Each permission: toggle switch + display name + description tooltip
- Footer actions: "Copy permissions from…" (opens CopyFromDialog) and "Reset to defaults" (opens ResetRoleDialog with confirmation)

Toggling a permission calls `useGrantPermission` / `useRevokePermission` with optimistic update. The ADMIN card disables the MANAGE_USERS toggle (cannot revoke) and shows a lock icon.

### Users Page Extension

Add a "Permissions" expandable row or popover per user showing their role's effective permissions (read-only, derived from role). No per-user override UI.

### SignalR Integration

In `src/features/signalr/eventRouter.ts`, add handler for `PermissionChangedEvent`:

```typescript
case 'PermissionChangedEvent':
  queryClient.invalidateQueries({ queryKey: queryKeys.permissions() });
  queryClient.invalidateQueries({ queryKey: queryKeys.roles() });
  break;
```

---

## Testing

### Backend Unit Tests (`MSOSync.MetadataTests/Permissions/`)

All use `TestDbContext.Create()` (SQLite in-memory). Seed the three roles and M018 permission defaults before each test.

| Test | Assertion |
|---|---|
| GetEffectivePermissions — VIEWER | returns VIEW_EVENTS, VIEW_METRICS, VIEW_AUDIT, VIEW_TOPOLOGY |
| GetEffectivePermissions — OPERATOR | returns VIEWER set + EXPORT_DATA, RETRY_BATCHES, APPROVE_NODES, RELEASE_LOCKS |
| GetEffectivePermissions — ADMIN | returns all 12 permissions |
| Grant — adds permission | GetEffectivePermissions reflects new key |
| Revoke — removes permission | GetEffectivePermissions excludes key |
| Revoke MANAGE_USERS from ADMIN | throws ValidationException with code PERMISSION_PROTECTED |
| Reset — restores M018 defaults | after grant+revoke, reset returns to seed state |
| CopyFrom — replaces target | VIEWER gains OPERATOR's set; old VIEWER-only permissions removed |
| CopyFrom — idempotent | calling twice produces same result |
| GetAllPermissions | returns 12 catalog entries with all fields populated |
| Grant triggers audit | SyncAudit table has GRANT_PERMISSION entry |
| Revoke triggers audit | SyncAudit table has REVOKE_PERMISSION entry |
| Cache eviction on write | IMemoryCache entry for roleName absent after grant |

### Backend Integration Tests (`MSOSync.IntegrationTests/Permissions/`)

Use LocalDB (or Testcontainers where available).

| Test | Assertion |
|---|---|
| GET /me/permissions — VIEWER JWT | 200, permissions = VIEW_* only |
| GET /me/permissions — OPERATOR JWT | 200, includes RETRY_BATCHES |
| GET /me/permissions — ADMIN JWT | 200, all 12 |
| PUT /roles/VIEWER/permissions/RETRY_BATCHES — ADMIN JWT | 200; subsequent GET /me/permissions as VIEWER includes RETRY_BATCHES |
| DELETE /roles/ADMIN/permissions/MANAGE_USERS — ADMIN JWT | 400 PERMISSION_PROTECTED |
| POST /roles/OPERATOR/reset — ADMIN JWT | 200; GET /roles/OPERATOR returns seed defaults |
| POST /roles/VIEWER/copy-from/OPERATOR — ADMIN JWT | 200; VIEWER has OPERATOR's permission set |
| PUT /roles/VIEWER/permissions/RETRY_BATCHES — VIEWER JWT | 403 |
| PermissionChangedEvent emitted on grant | SignalR hub receives event after PUT grant |
| Audit entry written | SyncAudit has GRANT_PERMISSION record after successful PUT |

### Frontend Smoke Tests

- `useHasPermission` returns `false` while `usePermissions` is loading (verified by mocking pending query state)
- Direct navigation to `/administration/roles` without MANAGE_USERS renders `<PermissionDeniedPage />`
- Retry button renders as disabled with tooltip when user lacks RETRY_BATCHES

---

## Migration Sequence

1. M018: Create `sync_permission`, `sync_role_permission`, seed all 12 permissions + 3-role defaults
2. Register `IPermissionService` in `MetadataServiceExtensions.cs`
3. Add `PermissionsController` and `RolesController` to `MSOSync.Api`
4. Add `PermissionChangedEvent` to `ISyncEventPublisher` / SignalR hub
5. Frontend: types → API functions → hooks → AppLayout prefetch → route guards → page gates → Roles admin page

No changes to existing JWT generation, existing `[Authorize]` policies, or existing role assignment flow.
