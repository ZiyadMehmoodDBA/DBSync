# Epic 13: Alerting & Notification Center — Design Spec

**Date:** 2026-07-14  
**Status:** Approved

---

## Goal

Deliver an in-app notification system that automatically converts critical domain events into persisted per-user notifications, surfaced via a notification bell in the navigation bar and a dedicated Notifications page.

## Scope

In-app delivery only. Email and webhook channels are deferred. No user-defined alert rules — specific event types are hardcoded to always produce notifications. Notifications accumulate without auto-deletion (retention cleanup deferred).

---

## Architecture

### Layers

```
Domain Event (MediatR INotification)
        │
        ▼
Notification Handler (MSOSync.Metadata/Notifications/Handlers/)
        │  calls NotificationService.CreateAsync(...)
        ▼
NotificationService
        │  inserts sync_notification + fan-out sync_user_notification rows
        ▼
OperationsHub.NotifyNotificationCreated(userId, dto)
        │  SignalR M12 push per user
        ▼
Frontend SignalR hook → bell badge update + toast
```

### Project placement

- Backend: `MSOSync.Metadata` (service + handlers + query service + DTOs)
- Controller: `MSOSync.Api` (existing controllers assembly)
- Frontend: `MSOSync.Frontend/src/features/notifications/`
- Migration: `MSOSync.Persistence` (M028)

---

## Data Model

### Migration M028 — two new tables

```sql
CREATE TABLE msosync.sync_notification (
    notification_id BIGINT IDENTITY(1,1) NOT NULL,
    event_type      NVARCHAR(50)   NOT NULL,   -- WORKER_FAILED, NODE_UNREACHABLE, ...
    severity        NVARCHAR(20)   NOT NULL,   -- Critical | Warning | Info | Security
    title           NVARCHAR(200)  NOT NULL,
    body            NVARCHAR(1000) NOT NULL,
    target_route    NVARCHAR(200)  NULL,       -- deep-link, e.g. /admin/system
    correlation_id  NVARCHAR(100)  NULL,
    created_at      DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_sync_notification PRIMARY KEY (notification_id)
);

CREATE TABLE msosync.sync_user_notification (
    user_id         INT    NOT NULL,
    notification_id BIGINT NOT NULL,
    is_read         BIT    NOT NULL DEFAULT 0,
    read_at         DATETIME2 NULL,
    CONSTRAINT PK_sync_user_notification PRIMARY KEY (user_id, notification_id),
    CONSTRAINT FK_sun_user    FOREIGN KEY (user_id)         REFERENCES msosync.sync_user(user_id),
    CONSTRAINT FK_sun_notif   FOREIGN KEY (notification_id) REFERENCES msosync.sync_notification(notification_id)
        ON DELETE CASCADE
);

CREATE INDEX IX_sun_user_unread ON msosync.sync_user_notification (user_id, is_read, notification_id DESC);
```

### EF Entity types

`SyncNotification` and `SyncUserNotification` in `MSOSync.Persistence/Entities/`.

---

## Backend

### NotificationAudience enum

```csharp
public enum NotificationAudience { All, OperatorAndAbove, AdminOnly }
```

Audience resolution: `NotificationService` queries `SyncUser JOIN SyncUserRole JOIN SyncRole WHERE Enabled = true` and filters by role name — `All` = every enabled user, `OperatorAndAbove` = users with role OPERATOR or ADMIN, `AdminOnly` = users with role ADMIN.

### INotificationService

```csharp
public interface INotificationService
{
    Task CreateAsync(
        string eventType, string severity, string title, string body,
        string? targetRoute, string? correlationId,
        NotificationAudience audience,
        CancellationToken ct = default);
}
```

`NotificationService` (scoped):
1. Inserts one `SyncNotification` row.
2. Queries `SyncUser` where `Enabled = true`, filtered by audience role.
3. Bulk-inserts `SyncUserNotification` rows.
4. For each affected user, calls `IOperationsHubPublisher.NotifyNotificationCreated(userId, dto)` (fire-and-forget, errors logged not thrown).

### INotificationQueryService

```csharp
public interface INotificationQueryService
{
    Task<NotificationPageDto> GetPagedAsync(int userId, int page, int pageSize, CancellationToken ct);
    Task<int>  GetUnreadCountAsync(int userId, CancellationToken ct);
    Task       MarkReadAsync(int userId, long notificationId, CancellationToken ct);
    Task       MarkAllReadAsync(int userId, CancellationToken ct);
}
```

### DTOs

```csharp
public sealed record NotificationDto(
    long   NotificationId,
    string EventType,
    string Severity,
    string Title,
    string Body,
    string? TargetRoute,
    string? CorrelationId,
    DateTime CreatedAt,
    bool   IsRead,
    DateTime? ReadAt);

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationDto> Items,
    int Page,
    int PageSize,
    int TotalUnread);

public sealed record NotificationPushDto(
    long   NotificationId,
    string Severity,
    string Title,
    int    UnreadCount);   // total unread for this user after creation
```

### NotificationController — `/api/v1/notifications`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/` | Any role | Paged list. Query params: `page=1`, `pageSize=20`, `unreadOnly=false` |
| GET | `/unread-count` | Any role | Returns `{ "count": N }` |
| POST | `/{id}/read` | Any role | Marks one notification read |
| POST | `/read-all` | Any role | Marks all of current user's notifications read |

All routes require JWT. Current user resolved via `ICurrentUserService`.

### MediatR Notification Handlers

Ten handlers in `MSOSync.Metadata/Notifications/Handlers/`. All implement `INotificationHandler<TEvent>` and inject `INotificationService`.

| Handler | Trigger Condition | EventType constant | Severity | Audience |
|---------|------------------|--------------------|----------|----------|
| `WorkerFailedNotificationHandler` | `WorkerStatusChangedEvent` where `NewState == WorkerHealthState.Failed` | `WORKER_FAILED` | Critical | All |
| `WorkerWarningNotificationHandler` | `WorkerStatusChangedEvent` where `NewState == WorkerHealthState.Warning` | `WORKER_WARNING` | Warning | OperatorAndAbove |
| `NodeUnreachableNotificationHandler` | `NodeConnectivityChangedEvent` where `Status == ConnectivityStatus.Unreachable` | `NODE_UNREACHABLE` | Warning | All |
| `NodeRecoveryNotificationHandler` | `NodeLifecycleChangedEvent` where `NewState == NodeLifecycleState.Recovery` | `NODE_IN_RECOVERY` | Warning | OperatorAndAbove |
| `NodeRejectedNotificationHandler` | `NodeLifecycleChangedEvent` where `NewState == NodeLifecycleState.Rejected` | `NODE_REJECTED` | Info | All |
| `NodeDecommissionedNotificationHandler` | `NodeLifecycleChangedEvent` where `NewState == NodeLifecycleState.Decommissioned` | `NODE_DECOMMISSIONED` | Info | All |
| `SchedulerRecoveryNotificationHandler` | `SchedulerRecoveryEvent` | `SCHEDULER_RECOVERED` | Warning | AdminOnly |
| `AccountLockedNotificationHandler` | `AccountLockedEvent` | `ACCOUNT_LOCKED` | Security | AdminOnly |
| `TokenReuseNotificationHandler` | `TokenReuseDetectedEvent` | `TOKEN_REUSE_DETECTED` | Security | AdminOnly |
| `OperationFailedNotificationHandler` | `OperationChangedEvent` where `Status == "Failed"` | `OPERATION_FAILED` | Warning | OperatorAndAbove |

### SignalR — M12 NotificationReceived

Add `NotificationReceived = 12` to the existing `OperationsEventType` enum.

`IOperationsHubPublisher` gains:
```csharp
Task NotifyNotificationCreated(int userId, NotificationPushDto dto);
```

`OperationsHubPublisher` implementation sends to the connection group for that specific user (user group key: `$"user-{userId}"`). Frontend clients join their user group on connect (in addition to the existing "operators" group).

---

## Frontend

### File structure

```
src/features/notifications/
  NotificationsPage.tsx          — full notifications page (/notifications)
  NotificationBell.tsx           — bell icon + badge + popover
  NotificationItem.tsx           — single notification row
  hooks.ts                       — useUnreadCount, useNotifications, useMarkRead, useMarkAllRead
  api.ts                         — API client functions
  types.ts                       — NotificationDto, NotificationPageDto
```

### NotificationBell

- Positioned in existing `AppShell` top navigation, right of search / left of user avatar
- `useUnreadCount()` fetches `/api/v1/notifications/unread-count` on mount (staleTime 60s)
- Badge renders when count > 0, shows count (capped at "99+")
- Click → Popover showing last 5 notifications (`pageSize=5, page=1`)
- Popover footer: "Mark all read" button + "View all notifications →" link to `/notifications`
- M12 SignalR event increments count optimistically and triggers toast (using existing toast infrastructure)

### /notifications page

- Accessible to all authenticated users
- Tab filters: All / Unread / Critical / Security
- List items: severity-colored left border, title (bold if unread), body (truncated 120 chars), time ago, click → marks read + follows `targetRoute` if present
- "Mark all read" button in page header (disabled if unreadCount === 0)
- Pagination: page-based, 20 per page, previous/next controls
- Empty state: "No notifications" with icon

### Hooks

```typescript
// useUnreadCount — integrates with M12 SignalR push
function useUnreadCount(): number

// useNotifications — TanStack Query paginated
function useNotifications(filter: 'all' | 'unread' | 'critical' | 'security', page: number):
  UseQueryResult<NotificationPageDto>

// mutations
function useMarkRead(): UseMutationResult<void, Error, { notificationId: number }>
function useMarkAllRead(): UseMutationResult<void, Error, void>
```

`useMarkRead` and `useMarkAllRead` invalidate both `['notifications']` and `['unread-count']` query keys.

### SignalR integration

In the existing `useOperationsEvents` hook (or wherever M1–M11 are handled), add M12 handler:
```typescript
case OperationsEventType.NotificationReceived:
  queryClient.setQueryData(['unread-count'], payload.unreadCount);
  toast({ title: payload.title, variant: severityToVariant(payload.severity) });
  queryClient.invalidateQueries({ queryKey: ['notifications'] });
  break;
```

User-specific SignalR group: on hub connect, server adds connection to `$"user-{userId}"` group. Frontend needs no changes — the hub handles grouping server-side.

---

## DI Registration

In `MetadataServiceExtensions.AddMetadata(...)`:
```csharp
services.AddScoped<INotificationService, NotificationService>();
services.AddScoped<INotificationQueryService, NotificationQueryService>();
// MediatR handlers auto-discovered via assembly scan (already registered)
```

---

## Testing

**Unit tests** (`MSOSync.MetadataTests`):
- `NotificationServiceTests` — verify fan-out creates correct number of `SyncUserNotification` rows; verify audience filtering (All vs OperatorAndAbove vs AdminOnly)
- `WorkerFailedNotificationHandlerTests` — verify handler calls service with correct eventType/severity
- `NotificationQueryServiceTests` — verify paging, unread count, mark-read

**Integration tests** (`MSOSync.IntegrationTests`):
- `NotificationControllerTests` — GET `/notifications`, GET `/unread-count`, POST `/{id}/read`, POST `/read-all` — verify auth, correct data, unread count decrements

---

## Global Constraints

- Zero build warnings
- No new features beyond spec
- `TreatWarningsAsErrors` enforced
- Do not commit `.env` files or secrets
- Stage files by name only
- Branch: main

---

## Out of Scope (deferred)

- Email / SMTP delivery
- Webhook delivery
- User notification preferences (opt-in/opt-out per type)
- Notification retention / auto-cleanup
- Alert silencing / snooze
- Escalation rules
