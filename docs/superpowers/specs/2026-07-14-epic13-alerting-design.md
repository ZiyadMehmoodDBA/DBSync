# Epic 13: Alerting & Notification Center — Design Spec

**Date:** 2026-07-14  
**Status:** Approved (v2 — post-review refinements)

---

## Goal

Deliver an in-app notification system that automatically converts critical domain events into persisted per-user notifications, surfaced via a notification bell in the navigation bar and a dedicated Notifications page.

## Scope

In-app delivery only. Email and webhook channels are deferred. No user-defined alert rules — specific event types trigger notifications automatically. Notifications accumulate without auto-deletion (retention deferred; `IsArchived` is included today to make retention cheap later). `sync_user_notification_preferences` is reserved but not implemented.

---

## Architecture

```
Domain Event (MediatR INotification)
        │
        ▼
Notification Handler  (MSOSync.Metadata/Notifications/Handlers/)
        │  calls INotificationService.CreateAsync(...)
        ▼
NotificationService
        │  1. dedup check (same DedupKey within 10 min window → bump OccurrenceCount)
        │  2. INSERT sync_notification
        │  3. bulk INSERT sync_user_notification rows (AddRange + single SaveChangesAsync)
        │  4. publish NotificationCreatedDomainEvent via MediatR
        ▼
NotificationPublisher : INotificationHandler<NotificationCreatedDomainEvent>
        │  calls IOperationsHubPublisher.NotifyNotificationCreated(userId, dto)
        │  fire-and-forget per user; errors logged, not thrown
        ▼
OperationsHub → SignalR M12 push to "user-{userId}" group
        │
        ▼
Frontend SignalR hook → bell badge update + toast
```

`NotificationService` writes data only. `NotificationPublisher` handles all SignalR delivery — same pattern as `NodeLifecycleService` → `NodeOperationsPublisher`. Service layer never touches the hub directly.

### Project placement

- Domain events + handlers + services: `MSOSync.Metadata`
- Controller: `MSOSync.Api` (existing controllers assembly)
- Frontend: `MSOSync.Frontend/src/features/notifications/`
- Migration: `MSOSync.Persistence` (M028)

---

## Enums

### NotificationEventType

```csharp
public enum NotificationEventType
{
    WorkerFailed,
    WorkerWarning,
    NodeUnreachable,
    NodeInRecovery,
    NodeRejected,
    NodeDecommissioned,
    SchedulerRecovered,
    AccountLocked,
    TokenReuseDetected,
    OperationFailed
}
```

EF config: `HasConversion<string>()` — persisted as the enum member name string.

### NotificationSeverity

```csharp
public enum NotificationSeverity { Info, Warning, Critical, Security }
```

EF config: `HasConversion<string>()`.

### NotificationAudience

```csharp
public enum NotificationAudience { AllUsers, Operators, Administrators }
```

Audience resolution in `NotificationService`: query `SyncUser JOIN SyncUserRole JOIN SyncRole WHERE Enabled = true`, filter by role name — `AllUsers` = all enabled users, `Operators` = OPERATOR or ADMIN roles, `Administrators` = ADMIN role only.

---

## Data Model

### Migration M028

```sql
CREATE TABLE msosync.sync_notification (
    notification_id   BIGINT IDENTITY(1,1) NOT NULL,
    event_type        NVARCHAR(50)   NOT NULL,   -- NotificationEventType enum name
    severity          NVARCHAR(20)   NOT NULL,   -- NotificationSeverity enum name
    title             NVARCHAR(200)  NOT NULL,
    body              NVARCHAR(1000) NOT NULL,
    source_entity_type NVARCHAR(50)  NULL,       -- Node | Worker | Operation | Template
    source_entity_id  NVARCHAR(200)  NULL,       -- e.g. nodeId, workerName, operationId
    dedup_key         NVARCHAR(260)  NULL,       -- "{EventType}:{SourceEntityId}" for deduplication
    occurrence_count  INT            NOT NULL DEFAULT 1,
    correlation_id    NVARCHAR(100)  NULL,
    created_at        DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    last_occurred_at  DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_sync_notification PRIMARY KEY (notification_id)
);

CREATE INDEX IX_sn_dedup ON msosync.sync_notification (dedup_key, created_at DESC)
    WHERE dedup_key IS NOT NULL;

CREATE TABLE msosync.sync_user_notification (
    user_id         INT      NOT NULL,
    notification_id BIGINT   NOT NULL,
    is_read         BIT      NOT NULL DEFAULT 0,
    read_at         DATETIME2 NULL,
    is_archived     BIT      NOT NULL DEFAULT 0,   -- reserved for future retention
    archived_at     DATETIME2 NULL,
    CONSTRAINT PK_sync_user_notification PRIMARY KEY (user_id, notification_id),
    CONSTRAINT FK_sun_user   FOREIGN KEY (user_id)         REFERENCES msosync.sync_user(user_id),
    CONSTRAINT FK_sun_notif  FOREIGN KEY (notification_id) REFERENCES msosync.sync_notification(notification_id)
        ON DELETE CASCADE
);

CREATE INDEX IX_sun_user_unread ON msosync.sync_user_notification (user_id, is_read, notification_id DESC);
```

### EF Entity types

`SyncNotification` and `SyncUserNotification` in `MSOSync.Persistence/Entities/`.

---

## Backend

### INotificationService

```csharp
public interface INotificationService
{
    Task CreateAsync(
        NotificationEventType eventType,
        NotificationSeverity  severity,
        string                title,
        string                body,
        string?               sourceEntityType,
        string?               sourceEntityId,
        string?               correlationId,
        NotificationAudience  audience,
        CancellationToken     ct = default);
}
```

**`NotificationService` (scoped) — implementation contract:**

1. Build `dedupKey = $"{eventType}:{sourceEntityId}"` (null if `sourceEntityId` is null).
2. If `dedupKey` is not null: query `sync_notification WHERE dedup_key = @key AND created_at >= NOW() - 10min`. If found: increment `OccurrenceCount`, set `LastOccurredAt = UtcNow`, call `SaveChangesAsync`, **return early** (no fan-out, no event).
3. Insert one `SyncNotification` row.
4. Query enabled users filtered by audience (single query with role join).
5. **Bulk insert** `SyncUserNotification` rows using `AddRange` + single `SaveChangesAsync` — never loop with individual saves.
6. Publish `NotificationCreatedDomainEvent(notificationId, [userIds], pushDto)` via `IPublisher`.

### NotificationCreatedDomainEvent

```csharp
public sealed record NotificationCreatedDomainEvent(
    long                  NotificationId,
    IReadOnlyList<int>    UserIds,
    NotificationPushDto   PushDto) : INotification;
```

### NotificationPublisher

```csharp
public sealed class NotificationPublisher(IOperationsHubPublisher hub, ILogger<NotificationPublisher> logger)
    : INotificationHandler<NotificationCreatedDomainEvent>
{
    public async Task Handle(NotificationCreatedDomainEvent evt, CancellationToken ct)
    {
        foreach (var userId in evt.UserIds)
        {
            try { await hub.NotifyNotificationCreated(userId, evt.PushDto); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to push notification {Id} to user {UserId}",
                    evt.NotificationId, userId);
            }
        }
    }
}
```

### INotificationQueryService

```csharp
public interface INotificationQueryService
{
    Task<NotificationPageDto> GetPagedAsync(
        int userId, string? cursor, int pageSize,
        bool unreadOnly, NotificationSeverity? severityFilter,
        CancellationToken ct);

    Task<int>  GetUnreadCountAsync(int userId, CancellationToken ct);
    Task       MarkReadAsync(int userId, long notificationId, CancellationToken ct);
    Task       MarkAllReadAsync(int userId, CancellationToken ct);
}
```

Pagination uses cursor-based pattern consistent with `EventQueryService`, `AuditQueryService`, etc. Cursor encodes `(notificationId, createdAt.Ticks)` via `CursorSigner`.

### DTOs

```csharp
public sealed record NotificationDto(
    long                  NotificationId,
    NotificationEventType EventType,
    NotificationSeverity  Severity,
    string                Title,
    string                Body,
    string?               SourceEntityType,
    string?               SourceEntityId,
    string?               CorrelationId,
    DateTime              CreatedAt,
    DateTime              LastOccurredAt,
    int                   OccurrenceCount,
    bool                  IsRead,
    DateTime?             ReadAt);

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationDto> Items,
    string?  NextCursor,
    int      TotalUnread);

public sealed record NotificationPushDto(
    long                  NotificationId,
    NotificationEventType EventType,
    NotificationSeverity  Severity,
    string                Title,
    string                Body,
    string?               SourceEntityType,
    string?               SourceEntityId,
    DateTime              CreatedAt,
    int                   UnreadCount);   // total unread for this user after creation
```

`NotificationPushDto` carries enough for the bell popover to render without a refetch.

### NotificationController — `/api/v1/notifications`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/` | Any role | Cursor-paged list. Params: `cursor`, `pageSize=20`, `unreadOnly=false`, `severity` |
| GET | `/unread-count` | Any role | Returns `{ "count": N }` |
| POST | `/{id}/read` | Any role | Marks one notification read |
| PATCH | `/{id}` | Any role | Body `{ "isRead": true/false }` — REST-idiomatic; supports future "mark unread" |
| POST | `/read-all` | Any role | Marks all of current user's notifications read |

All routes require JWT. Current user resolved via `ICurrentUserService.UserId`.

### MediatR Notification Handlers (10)

All in `MSOSync.Metadata/Notifications/Handlers/`, implement `INotificationHandler<TEvent>`, inject `INotificationService`.

| Handler | Trigger | EventType | Severity | SourceEntityType | Audience |
|---------|---------|-----------|----------|-----------------|----------|
| `WorkerFailedNotificationHandler` | `WorkerStatusChangedEvent` where `NewState == Failed` | `WorkerFailed` | Critical | Worker | AllUsers |
| `WorkerWarningNotificationHandler` | `WorkerStatusChangedEvent` where `NewState == Warning` | `WorkerWarning` | Warning | Worker | Operators |
| `NodeUnreachableNotificationHandler` | `NodeConnectivityChangedEvent` where `Status == Unreachable` | `NodeUnreachable` | Warning | Node | AllUsers |
| `NodeRecoveryNotificationHandler` | `NodeLifecycleChangedEvent` where `NewState == Recovery` | `NodeInRecovery` | Warning | Node | Operators |
| `NodeRejectedNotificationHandler` | `NodeLifecycleChangedEvent` where `NewState == Rejected` | `NodeRejected` | Info | Node | AllUsers |
| `NodeDecommissionedNotificationHandler` | `NodeLifecycleChangedEvent` where `NewState == Decommissioned` | `NodeDecommissioned` | Info | Node | AllUsers |
| `SchedulerRecoveryNotificationHandler` | `SchedulerRecoveryEvent` | `SchedulerRecovered` | Warning | Worker | Administrators |
| `AccountLockedNotificationHandler` | `AccountLockedEvent` | `AccountLocked` | Security | — | Administrators |
| `TokenReuseNotificationHandler` | `TokenReuseDetectedEvent` | `TokenReuseDetected` | Security | — | Administrators |
| `OperationFailedNotificationHandler` | `OperationChangedEvent` where `Status == "Failed"` | `OperationFailed` | Warning | Operation | Operators |

`SourceEntityId` maps to the natural identifier of the source: nodeId for Node, workerName for Worker, operationId (string) for Operation.

### SignalR — M12 NotificationReceived

Add `NotificationReceived = 12` to the existing `OperationsEventType` enum.

`IOperationsHubPublisher` gains:
```csharp
Task NotifyNotificationCreated(int userId, NotificationPushDto dto);
```

`OperationsHubPublisher` sends to `$"user-{userId}"` SignalR group.

**User group lifecycle:** On hub connect, `OnConnectedAsync` adds the connection to both the existing `"operators"` group **and** `$"user-{userId}"`. This means all browser tabs open for the same user (same `userId`, different `connectionId`) all receive their personal notifications. No frontend changes needed — the hub handles grouping server-side.

---

## Frontend

### File structure

```
src/features/notifications/
  NotificationsPage.tsx        — /notifications full page
  NotificationBell.tsx         — bell + badge + popover (5 most recent)
  NotificationItem.tsx         — single row component
  hooks.ts                     — useUnreadCount, useNotifications, useMarkRead, useMarkAllRead
  api.ts                       — typed API client
  types.ts                     — NotificationDto, NotificationPageDto, enums
  routing.ts                   — getTargetRoute(entityType, entityId) → string | null
```

### routing.ts — frontend derives routes from entity type

```typescript
export function getTargetRoute(
  entityType: string | null | undefined,
  entityId: string | null | undefined
): string | null {
  switch (entityType) {
    case 'Node':      return entityId ? `/operations/nodes/${entityId}` : '/operations/nodes';
    case 'Worker':    return '/admin/system';
    case 'Operation': return entityId ? `/operations/jobs/${entityId}` : '/operations/jobs';
    default:          return null;
  }
}
```

Backend stores entity type and id; frontend owns routing knowledge.

### NotificationBell

- Positioned in existing `AppShell` top navigation bar, right of search, left of user avatar
- `useUnreadCount()` fetches `GET /unread-count` on mount (`staleTime: 60_000`)
- Badge: visible when count > 0, capped at "99+"
- Click → Popover: 5 most recent notifications, "Mark all read" button, "View all →" link
- M12 SignalR event: update `unreadCount` query cache directly (`queryClient.setQueryData`) + show toast
- Clicking a notification item: `POST /{id}/read` then navigate to `getTargetRoute(...)` if not null

### /notifications page

- Accessible to all authenticated users (`/notifications` route)
- Tab filters: **All / Unread / Critical / Security**
- List rows: severity-colored left border, bold title if unread, body truncated at 120 chars, time-ago, OccurrenceCount badge when > 1
- Click row: marks read + navigates to deep link if available
- "Mark all read" in page header (disabled when unreadCount === 0)
- Cursor-based pagination: "Load more" button, `pageSize=20`
- Empty state: icon + "No notifications"

### Hooks

```typescript
// Fetches + listens to M12 for live count
function useUnreadCount(): number

// Cursor-paged, supports filter tabs
function useNotifications(
  filter: 'all' | 'unread' | 'critical' | 'security',
  pageSize?: number
): { items: NotificationDto[], loadMore: () => void, hasMore: boolean, isLoading: boolean }

// Mutations invalidate ['notifications'] and ['unread-count']
function useMarkRead(): UseMutationResult<void, Error, { notificationId: number }>
function useMarkAllRead(): UseMutationResult<void, Error, void>
```

### SignalR M12 integration

In the existing operations event handler map (where M1–M11 are handled):

```typescript
case OperationsEventType.NotificationReceived: {
  const payload = event.payload as NotificationPushDto;
  queryClient.setQueryData(['unread-count'], payload.unreadCount);
  queryClient.invalidateQueries({ queryKey: ['notifications'] });
  toast({
    title: payload.title,
    variant: severityToVariant(payload.severity),
    description: payload.body?.slice(0, 80)
  });
  break;
}
```

Helper:
```typescript
function severityToVariant(s: string): 'default' | 'destructive' | 'warning' {
  if (s === 'Critical' || s === 'Security') return 'destructive';
  if (s === 'Warning') return 'warning';
  return 'default';
}
```

---

## DI Registration

In `MetadataServiceExtensions.AddMetadata(...)`:

```csharp
services.AddScoped<INotificationService, NotificationService>();
services.AddScoped<INotificationQueryService, NotificationQueryService>();
// NotificationPublisher + 10 handlers auto-discovered via MediatR assembly scan
```

---

## Testing

**Unit tests** (`MSOSync.MetadataTests`):
- `NotificationServiceTests`:
  - Fan-out creates correct `SyncUserNotification` count for each `NotificationAudience` value
  - Deduplication: second call within 10-min window with same dedup_key increments `OccurrenceCount`, does not insert new row
  - Deduplication: call outside window creates new row
  - Bulk insert (single `SaveChangesAsync` call, not N individual calls)
- `WorkerFailedNotificationHandlerTests`: handler calls service with `WorkerFailed`, `Critical`, `AllUsers`
- `NotificationQueryServiceTests`:
  - Cursor pagination returns correct page and NextCursor
  - `unreadOnly=true` filters read notifications
  - `GetUnreadCountAsync` returns 0 after `MarkAllReadAsync`
  - Pagination ordering is consistent (descending by notificationId)

**Integration tests** (`MSOSync.IntegrationTests`):
- `NotificationControllerTests`:
  - `GET /notifications` requires auth, returns paged result
  - `GET /unread-count` returns correct integer
  - `POST /{id}/read` sets `IsRead=true`, decrements unread count
  - `POST /read-all` sets all `IsRead=true` for requesting user only, not other users
  - `PATCH /{id}` with `{ "isRead": true }` same effect as POST read
  - Concurrent `POST /{id}/read` for same id is idempotent (no 500)
  - SignalR push: `NotifyNotificationCreated` called with correct userId after `CreateAsync`

---

## Global Constraints

- Zero build warnings (`TreatWarningsAsErrors` enforced)
- No new features beyond this spec
- Do not commit `.env` files, secrets, or plaintext credentials
- Stage files by name only — never `git add .` or `git add -A`
- Branch: main

---

## Out of Scope (deferred)

- Email / SMTP delivery
- Webhook delivery
- `sync_user_notification_preferences` table (reserved; service intentionally designed for future preference filtering injection point at `NotificationService.CreateAsync`)
- Notification retention / auto-archive worker (columns `IsArchived`/`ArchivedAt` present today)
- Alert silencing / snooze
- Escalation rules
- Per-user opt-out per notification type
