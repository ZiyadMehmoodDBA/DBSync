# Epic 11C: SignalR Observability & Real-Time Design

## Goal

Add real-time push notifications to the MSOSync operator console using SignalR: live cache invalidation on node health and lifecycle events, toast notifications with deduplication, and a resilient hybrid model where SignalR is the primary freshness mechanism with 5-minute polling as a fallback.

## Architecture

```
Backend domain events (MediatR)
    ↓
NodeOperationsPublisher / SyncOperationsPublisher
    ↓
OperationsHub → Clients.Group("operators")
    ↓
@microsoft/signalr (frontend)
    ↓
SignalRProvider
    ├── routeToCache()  → React Query invalidation
    └── routeToToast()  → Sonner notifications
```

SignalR is a **cache invalidation bus**. REST APIs remain the source of truth. No data is transported over SignalR — only event signals that trigger targeted refetches.

## Tech Stack

- `Microsoft.AspNetCore.SignalR` (built-in, no NuGet install needed in .NET 9)
- `@microsoft/signalr` ^8.x (new frontend dep)
- Sonner (already installed in 10C)
- React Query 5 (already installed)

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`
- TypeScript strict, no `any`
- Relative imports in all feature code (no `@/` aliases in `shared/signalr/`)
- Hub requires `[Authorize(Policy = "ViewerOrAbove")]`; JWT accepted via query string only for `/hubs/*` paths
- `Clients.Group("operators")` — never `Clients.All`
- `accessTokenFactory` on frontend — never captured token in URL
- Single hub message channel: `"OperationsEvent"` — never per-event-type method names
- `OperationsEventType` enum serialized as strings via `JsonStringEnumConverter`
- Reconnect delays: `[0, 2_000, 5_000, 10_000, 30_000]` — never framework defaults
- Deduplication window: 30s bucket keyed by `${type}:${nodeId}:${currentStatus}:${bucket}`
- `routeToCache` returns `Promise<void>` to preserve future ordering semantics

---

## Backend Design

### File Structure

```
src/MSOSync.App/
  Hubs/
    OperationsHub.cs
  SignalR/
    OperationsEvent.cs
    OperationsEventType.cs
    NodeOperationsPublisher.cs
    SyncOperationsPublisher.cs
```

### OperationsEventType

```csharp
public enum OperationsEventType
{
    NodeHealthChanged,
    NodeRegistered,
    NodeApproved,
    NodeRejected,
    NodeDisabled,
    NodeEnabled,
    SyncCycleCompleted
}
```

### OperationsEvent

```csharp
public sealed record OperationsEvent(
    OperationsEventType Type,
    string NodeId,
    string? NodeLabel,
    string? PreviousStatus,
    string? CurrentStatus,
    DateTimeOffset OccurredAt,
    string? GroupId = null
);
```

`GroupId` is included now (even though unused in 11C) to enable future topology subgraph invalidation without a breaking DTO change.

### OperationsHub

```csharp
[Authorize(Policy = "ViewerOrAbove")]
public sealed class OperationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "operators");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "operators");
        await base.OnDisconnectedAsync(exception);
    }
}
```

No client-invokable methods. Pure server push.

Mapped at:
```csharp
app.MapHub<OperationsHub>("/hubs/operations");
```

### NodeOperationsPublisher

Implements `INotificationHandler<T>` for all six node domain events:
- `NodeConnectivityChangedEvent` → `OperationsEventType.NodeHealthChanged`
- `NodeRegisteredEvent` → `OperationsEventType.NodeRegistered`
- `NodeApprovedEvent` → `OperationsEventType.NodeApproved`
- `NodeRejectedEvent` → `OperationsEventType.NodeRejected`
- `NodeDisabledEvent` → `OperationsEventType.NodeDisabled`
- `NodeEnabledEvent` → `OperationsEventType.NodeEnabled`

**Prerequisite:** Verify `NodeEnabledEvent` exists as a first-class MediatR `INotification` before Task 1 begins. If node re-enablement is currently implemented as a command without a corresponding notification, add `NodeEnabledEvent` and its `Publish` call as part of Task 1 scope.

Injects `IHubContext<OperationsHub>`. Each handler maps the domain event fields to `OperationsEvent` and calls `Clients.Group("operators").SendAsync("OperationsEvent", dto, ct)`.

### SyncOperationsPublisher

Implements `INotificationHandler<SyncCycleCompletedEvent>` → `OperationsEventType.SyncCycleCompleted`. `NodeId` is `"system"`, `GroupId` is `"global"`, all other node fields null. Using `"global"` (not null) avoids future null-checks when subgraph invalidation arrives.

### JWT Configuration

Added to `JwtBearerOptions.Events` in the existing JWT bearer configuration (add `OnMessageReceived` if not already present):

```csharp
OnMessageReceived = context =>
{
    var accessToken = context.Request.Query["access_token"];
    var path = context.HttpContext.Request.Path;
    if (!string.IsNullOrEmpty(accessToken) &&
        path.StartsWithSegments("/hubs"))
    {
        context.Token = accessToken;
    }
    return Task.CompletedTask;
}
```

Scoped to `/hubs` prefix only. Existing REST auth unchanged.

### SignalR DI + JSON

```csharp
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters
            .Add(new JsonStringEnumConverter());
    });
```

---

## Frontend Design

### File Structure

```
src/MSOSync.Frontend/src/shared/signalr/
  types.ts          — OperationsEvent, OperationsEventType, SignalRContextValue
  context.ts        — React context + useSignalRContext hook
  useSignalR.ts     — HubConnection lifecycle
  SignalRProvider.tsx — composition, mounts connection, routes events
  eventRouter.ts    — routeToCache() + invalidation group functions
  notifications.ts  — routeToToast() + deduplication
```

### Provider Order

```tsx
<QueryClientProvider client={queryClient}>
  <AuthProvider>
    <SignalRProvider>
      <RouterProvider router={router} />
    </SignalRProvider>
  </AuthProvider>
</QueryClientProvider>
```

`SignalRProvider` sits inside `AuthProvider` so it can call `useAuth()` to obtain `getAccessToken()`.

### types.ts

```ts
export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected';

export interface SignalRContextValue {
  connectionState: ConnectionState;
  lastConnectedAt?: Date;
  lastDisconnectedAt?: Date;
}

export const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000, 30_000] as const;

export interface OperationsEvent {
  type: OperationsEventType;
  nodeId: string;
  nodeLabel: string | null;
  previousStatus: string | null;
  currentStatus: string | null;
  occurredAt: string; // ISO 8601
  groupId: string | null;
}

export enum OperationsEventType {
  NodeHealthChanged  = 'NodeHealthChanged',
  NodeRegistered     = 'NodeRegistered',
  NodeApproved       = 'NodeApproved',
  NodeRejected       = 'NodeRejected',
  NodeDisabled       = 'NodeDisabled',
  NodeEnabled        = 'NodeEnabled',
  SyncCycleCompleted = 'SyncCycleCompleted',
}
```

### useSignalR.ts

Builds and manages `HubConnection`:

```ts
const connection = new HubConnectionBuilder()
  .withUrl('/hubs/operations', {
    accessTokenFactory: () => getAccessToken() ?? '',
  })
  .withAutomaticReconnect(RECONNECT_DELAYS)
  .build();
```

- Started after `AuthProvider` confirms authenticated state
- Stopped on logout (called from `SignalRProvider` when auth state clears)
- Fires `onEvent(event: OperationsEvent)` callback on each `"OperationsEvent"` message
- Updates `connectionState` via `onreconnecting`, `onreconnected`, `onclose` hooks
- Updates `lastConnectedAt` / `lastDisconnectedAt` on state transitions
- On `onreconnected`: calls `queryClient.invalidateQueries()` (no filter) for a full consistency sweep — reconnect may have missed events during downtime. **Rule: incremental invalidation while connected; full sweep after reconnect.**

### SignalRProvider.tsx

Composes `useSignalR`, `routeToCache`, `routeToToast`. On each event:

```ts
await routeToCache(queryClient, event);
routeToToast(event);
```

Provides `SignalRContextValue` via context. Does not render any UI itself.

### eventRouter.ts

```ts
export async function routeToCache(
  queryClient: QueryClient,
  event: OperationsEvent,
): Promise<void> {
  switch (event.type) {
    case OperationsEventType.NodeHealthChanged:
      return invalidateNodeHealth(queryClient);
    case OperationsEventType.NodeRegistered:
    case OperationsEventType.NodeApproved:
    case OperationsEventType.NodeRejected:
    case OperationsEventType.NodeDisabled:
    case OperationsEventType.NodeEnabled:
      return invalidateNodeLifecycle(queryClient);
    case OperationsEventType.SyncCycleCompleted:
      return invalidateOperational(queryClient);
  }
}
```

**Invalidation groups:**

| Group | Query keys invalidated |
|---|---|
| `invalidateNodeHealth` | `nodes`, `topologyGraph`, `topologySummary`, `metricsSummary`, `dashboardSummary` |
| `invalidateNodeLifecycle` | `nodes`, `nodeGroups`, `topologyGraph`, `topologyGroups`, `dashboardSummary`, `metricsSummary` |
| `invalidateOperational` | `dashboardSummary`, `events`, `incomingBatches`, `outgoingBatches`, `batchErrors`, `metricsSummary` |

Note: trigger-related invalidation (`triggers`, `topologyGraph`, `topologySummary`, `topologyGroups`) is handled by existing mutation helpers in 10C. No `TriggerChanged` event needed from SignalR — mutations already call `invalidateTriggerRelated()`.

### notifications.ts

Toast severity mapping:

| Transition | Sonner call |
|---|---|
| Reachable → Degraded | `toast.warning("Node X is degraded.")` |
| Degraded → Unreachable | `toast.error("Node X is unreachable.")` |
| * → Reachable | `toast.success("Node X is reachable again.")` |
| NodeRegistered | `toast.info("New node X registered and awaits approval.")` |
| NodeApproved | `toast.success("Node X approved.")` |
| NodeRejected | `toast.warning("Node X registration rejected.")` |
| NodeDisabled | `toast.warning("Node X disabled.")` |
| NodeEnabled | `toast.info("Node X re-enabled.")` |
| SyncCycleCompleted | (no toast — silent cache invalidation only) |

**Deduplication:**

```ts
const bucket = Math.floor(new Date(event.occurredAt).getTime() / 30_000);
const key = `${event.type}:${event.nodeId}:${event.currentStatus}:${bucket}`;
```

`Map<string, true>` stores seen keys. If key exists, suppress toast. If key is new, show toast and store key. Map is bounded: if `seen.size > 1000`, call `seen.clear()` before inserting — prevents unbounded growth in long-running console sessions.

### Connection State Indicator

Small component mounted in app shell / top bar. Reads `connectionState` from `useSignalRContext()`.

- `connected` → hidden (no visual noise in normal operation)
- `reconnecting` → amber dot + "Reconnecting…" text
- `disconnected` → red dot + "Offline" text

### Polling Policy Changes

**Live pages** (dashboard, metrics):
```ts
refetchInterval: 300_000,  // 5 min
refetchOnWindowFocus: true,
staleTime: 60_000,
```

**Historical pages** (events, incoming/outgoing batches, batch errors, audit):
```ts
refetchInterval: false,
refetchOnWindowFocus: true,
staleTime: 0,
```

**Metadata pages** (users, parameters, channels, routers, triggers):
```ts
refetchInterval: false,
refetchOnWindowFocus: false,
staleTime: Infinity,
```

Dashboard `refetchInterval` changes from `30_000` → `300_000`. All others change from `30_000` or existing value as shown.

---

## Testing

### Backend Unit Tests

`NodeOperationsPublisherTests` and `SyncOperationsPublisherTests`. Mock `IHubContext<OperationsHub>`. Per test: publish domain event → assert `SendAsync("OperationsEvent", dto)` called with correct `Type`, `NodeId`, `NodeLabel`, `PreviousStatus`, `CurrentStatus`, `OccurredAt`, `GroupId`.

### Backend Integration Tests

Two independent tests:
1. Authenticated client connects → hub assigns to group → domain event published → client receives `OperationsEvent`
2. Anonymous client negotiates → `401`

### Frontend Unit Tests

**`eventRouter.test.ts`:** Mock `QueryClient.invalidateQueries`. One test per event type, assert exact query keys invalidated (no extras).

**`notifications.test.ts`:**
- Each toast severity mapping (8 cases)
- Deduplication: same key in same bucket → suppressed
- Deduplication: same key in next bucket → shown
- Same node, different `currentStatus` → different key → never suppressed

**`types.test.ts`:**
```ts
expect(RECONNECT_DELAYS).toEqual([0, 2_000, 5_000, 10_000, 30_000]);
```
Protects the explicit reconnect contract from accidental changes.

**`useSignalR.test.ts` (reconnect recovery):**
Simulate `onreconnected` firing on a mock connection → assert `queryClient.invalidateQueries()` called with no filter (full sweep). This is the most important resilience guarantee: a reconnect that missed events must trigger a full consistency sweep.

---

## Manual Acceptance Checklist

```
✓ Login → connection state silent (connected)
✓ Logout → connection stops cleanly
✓ Server restart → reconnects within 30s
✓ Token refresh → reconnect succeeds (no 401)
✓ Browser sleep/wake → reconnect succeeds
✓ Node health change → toast + dashboard + topology refresh
✓ Node approved → toast + nodes grid refresh
✓ Sync cycle → dashboard + batch pages refresh (no toast)
✓ Rapid node flapping → no toast storm (dedup suppresses)
✓ Same node, different status → second toast shown
✓ Anonymous browser tab → 401 on hub connect
✓ SignalR disconnected 5+ min → dashboard counters update via polling
✓ SignalR reconnect → full invalidateQueries() sweep fires
✓ SignalR reconnect → immediate invalidation resumes
```

---

## File Map

| File | Task | Action |
|---|---|---|
| `src/MSOSync.App/Hubs/OperationsHub.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/OperationsEventType.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/OperationsEvent.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/NodeOperationsPublisher.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/SyncOperationsPublisher.cs` | 1 | Create |
| `src/MSOSync.App/Program.cs` | 1 | Modify (AddSignalR, MapHub, JWT OnMessageReceived) |
| `tests/MSOSync.AppTests/SignalR/NodeOperationsPublisherTests.cs` | 1 | Create |
| `tests/MSOSync.AppTests/SignalR/SyncOperationsPublisherTests.cs` | 1 | Create |
| `tests/MSOSync.IntegrationTests/SignalR/OperationsHubTests.cs` | 1 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/types.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/context.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/notifications.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/features/dashboard/hooks.ts` | 3 | Modify (refetchInterval 30s→300s) |
| `src/MSOSync.Frontend/src/features/*/hooks.ts` | 3 | Modify (polling policy per tier) |
| `src/MSOSync.Frontend/src/App.tsx` (or main.tsx) | 2 | Modify (add SignalRProvider) |
| `src/MSOSync.Frontend/src/shared/components/AppShell.tsx` | 4 | Modify (connection indicator) |
| `src/MSOSync.Frontend/src/shared/signalr/useSignalR.test.ts` | 2 | Create (reconnect recovery test) |
| `src/MSOSync.Frontend/src/shared/signalr/eventRouter.test.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/notifications.test.ts` | 4 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/types.test.ts` | 2 | Create |
| `src/MSOSync.Frontend/package.json` | 2 | Modify (add @microsoft/signalr) |
