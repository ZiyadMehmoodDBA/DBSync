# Epic 10B: Read-Only Operator Console — Design Spec

> **For agentic workers:** Use superpowers:subagent-driven-development to implement this spec task-by-task.

**Goal:** Wire all 15 placeholder pages to live .NET read APIs, replacing every PlaceholderPage with real data using AG Grid tables, summary cards, and activity feeds. No mutations in this epic.

**Architecture:** Shared API functions + shared DTO types + centralized query key factory + feature-local React Query hooks. AG Grid used for all tabular data. Two grid patterns: server-side (paginated APIs) and client-side (list APIs).

**Tech Stack:** React 19, TypeScript strict, TanStack Query 5.101, AG Grid 35.3 (community), Tailwind 4, shadcn/ui, Lucide Icons, Vitest.

---

## Global Constraints

- TypeScript strict mode; no `any` types
- AG Grid community edition only — no enterprise features
- `PagedResult<T>` response shape → server-side AG Grid pagination (app owns page/pageSize state)
- `IReadOnlyList<T>` / array response shape → client-side AG Grid (AG Grid owns pagination/sort/filter)
- `DASHBOARD_REFRESH_MS = 30_000` — only Dashboard and Metrics pages auto-refresh; all others are user-driven
- `refetchIntervalInBackground: false` on all polling queries
- `refetchOnWindowFocus: false` on all non-polling queries
- `refetchOnWindowFocus: true` on Dashboard and Metrics polling queries
- `keepPreviousData: true` on all server-side paginated queries (prevents grid flash on page change)
- DTO types mirror backend contracts exactly — no UI fields (no `displayName`, `statusColor`, `formattedDate`)
- Format functions (dates, latency, queue depth) live in `shared/utils/`, never inline in grid column defs
- Query keys come exclusively from `shared/queryKeys.ts` — no inline key arrays elsewhere
- All new files under `src/MSOSync.Frontend/src/`
- `npm run build`, `npm run lint`, `npm test` must all pass after every task

---

## File Structure

```
src/MSOSync.Frontend/src/
├── shared/
│   ├── api/
│   │   ├── client.ts          ← existing
│   │   ├── auth.ts            ← existing
│   │   ├── dashboard.ts       ← new
│   │   ├── events.ts          ← new
│   │   ├── batches.ts         ← new (incoming, outgoing, errors)
│   │   ├── nodes.ts           ← new (nodes only, not groups)
│   │   ├── topology.ts        ← new (summary, groups, group nodes)
│   │   ├── metrics.ts         ← new
│   │   ├── channels.ts        ← new
│   │   ├── triggers.ts        ← new
│   │   ├── routers.ts         ← new
│   │   ├── users.ts           ← new
│   │   ├── parameters.ts      ← new
│   │   ├── audit.ts           ← new
│   │   └── locks.ts           ← new
│   │
│   ├── types/
│   │   ├── common.ts          ← PagedResult<T>, ApiError
│   │   ├── dashboard.ts
│   │   ├── events.ts
│   │   ├── batches.ts
│   │   ├── nodes.ts
│   │   ├── topology.ts
│   │   ├── metrics.ts
│   │   ├── channels.ts
│   │   ├── triggers.ts
│   │   ├── routers.ts
│   │   ├── users.ts
│   │   ├── parameters.ts
│   │   ├── audit.ts
│   │   ├── locks.ts
│   │   └── index.ts           ← re-exports all
│   │
│   ├── queryKeys.ts           ← centralized factory
│   │
│   ├── constants/
│   │   └── query.ts           ← DASHBOARD_REFRESH_MS = 30_000
│   │
│   ├── utils/
│   │   ├── date.ts            ← formatRelativeTime, formatDateTime
│   │   ├── numbers.ts         ← formatLatency, formatQueueDepth, formatPercent
│   │   └── status.ts          ← nodeStatusLabel, batchStatusLabel, statusVariant
│   │
│   └── components/
│       ├── data-display/
│       │   ├── DataGrid.tsx   ← client-side AG Grid wrapper
│       │   ├── ServerGrid.tsx ← server-side AG Grid + pagination controls
│       │   ├── SummaryCard.tsx
│       │   └── StatusBadge.tsx
│       └── feedback/
│           ├── ErrorState.tsx
│           └── EmptyState.tsx
│
└── features/
    ├── dashboard/
    │   ├── hooks.ts
    │   ├── SummaryCards.tsx
    │   ├── ActivityFeed.tsx
    │   └── DashboardPage.tsx
    ├── events/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── EventFilters.tsx
    │   ├── EventsGrid.tsx
    │   └── EventsPage.tsx
    ├── incoming-batches/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── IncomingBatchFilters.tsx
    │   ├── IncomingBatchesGrid.tsx
    │   └── IncomingBatchesPage.tsx
    ├── outgoing-batches/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── OutgoingBatchFilters.tsx
    │   ├── OutgoingBatchesGrid.tsx
    │   └── OutgoingBatchesPage.tsx
    ├── batch-errors/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── BatchErrorFilters.tsx
    │   ├── BatchErrorsGrid.tsx
    │   └── BatchErrorsPage.tsx
    ├── nodes/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── NodesGrid.tsx
    │   └── NodesPage.tsx
    ├── topology/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── TopologySummaryCards.tsx
    │   ├── TopologyGroupsGrid.tsx
    │   └── TopologyPage.tsx
    ├── metrics/
    │   ├── hooks.ts
    │   ├── MetricsSummaryCards.tsx
    │   ├── NodeMetricsGrid.tsx
    │   ├── ChannelMetricsGrid.tsx
    │   └── MetricsPage.tsx
    ├── channels/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── ChannelsGrid.tsx
    │   └── ChannelsPage.tsx
    ├── triggers/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── TriggersGrid.tsx
    │   └── TriggersPage.tsx
    ├── routers/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── RoutersGrid.tsx
    │   └── RoutersPage.tsx
    ├── users/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── UsersGrid.tsx
    │   └── UsersPage.tsx
    ├── parameters/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── ParametersGrid.tsx
    │   └── ParametersPage.tsx
    ├── audit/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── AuditFilters.tsx
    │   ├── AuditGrid.tsx
    │   └── AuditPage.tsx
    ├── locks/
    │   ├── columns.ts
    │   ├── hooks.ts
    │   ├── LocksGrid.tsx
    │   └── LocksPage.tsx
    └── profile/
        └── ProfilePage.tsx
```

---

## Shared Layer

### `shared/types/common.ts`

```typescript
export interface PagedResult<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ApiError {
  title: string;
  status: number;
  detail?: string;
  traceId?: string;
}
```

### `shared/constants/query.ts`

```typescript
export const DASHBOARD_REFRESH_MS = 30_000;
export const DEFAULT_PAGE_SIZE = 50;
export const DEFAULT_BATCH_PAGE_SIZE = 20;
```

### `shared/queryKeys.ts`

```typescript
import type { EventFilter, IncomingBatchFilter, OutgoingBatchFilter,
  BatchErrorFilter, AuditFilter, UserFilter } from './types';

export const queryKeys = {
  dashboardSummary: () => ['dashboard-summary'] as const,
  dashboardActivity: (page: number) => ['dashboard-activity', page] as const,

  events: (filter: EventFilter) => ['events', filter] as const,
  event: (id: number) => ['event', id] as const,

  incomingBatches: (filter: IncomingBatchFilter) => ['incoming-batches', filter] as const,
  outgoingBatches: (filter: OutgoingBatchFilter) => ['outgoing-batches', filter] as const,
  batchErrors: (filter: BatchErrorFilter) => ['batch-errors', filter] as const,

  nodes: () => ['nodes'] as const,
  node: (id: string) => ['node', id] as const,

  topologySummary: () => ['topology-summary'] as const,
  topologyGroups: () => ['topology-groups'] as const,
  topologyGroupNodes: (groupId: string) => ['topology-group-nodes', groupId] as const,

  metricsSummary: () => ['metrics-summary'] as const,
  nodeMetrics: () => ['node-metrics'] as const,
  channelMetrics: () => ['channel-metrics'] as const,
  runtimeMetrics: () => ['runtime-metrics'] as const,

  channels: () => ['channels'] as const,
  triggers: () => ['triggers'] as const,
  routers: () => ['routers'] as const,

  users: (filter: UserFilter) => ['users', filter] as const,
  parameters: () => ['parameters'] as const,
  parameterDescriptors: () => ['parameter-descriptors'] as const,

  auditLog: (filter: AuditFilter) => ['audit', filter] as const,
  locks: () => ['locks'] as const,
};
```

### AG Grid Wrappers

**`shared/components/data-display/DataGrid.tsx`** — client-side wrapper:
- Props: `rowData`, `columnDefs`, `loading`, `height?` (default `'100%'`)
- Shows `EmptyState` when `rowData.length === 0` and not loading
- Shows `ErrorState` when error prop passed
- Sets `pagination={true}`, `paginationPageSize={20}`, `domLayout='autoHeight'` when no height
- Applies Tailwind-compatible AG Grid theme via `className="ag-theme-quartz"`
- Enables `quickFilterText` prop for search input

**`shared/components/data-display/ServerGrid.tsx`** — server-side wrapper:
- Props: `rowData`, `columnDefs`, `loading`, `total`, `page`, `pageSize`, `onPageChange`, `onPageSizeChange`
- Renders AG Grid with `pagination={false}` (app owns pagination)
- Renders pagination controls below the grid: `← prev  Page N of M  next →` + page size selector
- Shows `EmptyState` when `total === 0` and not loading
- Shows row range: "Showing 1–50 of 1,234"

**`shared/components/data-display/SummaryCard.tsx`** — metric card:
- Props: `title`, `value`, `subtitle?`, `icon?` (Lucide), `variant?: 'default' | 'success' | 'warning' | 'danger'`
- Uses shadcn `Card` component
- Color variants via Tailwind: green border for success, yellow for warning, red for danger

**`shared/components/data-display/StatusBadge.tsx`** — status pill:
- Props: `status: string`, `variant: 'success' | 'warning' | 'danger' | 'neutral'`
- Uses shadcn `Badge` component

**`shared/components/feedback/ErrorState.tsx`** — error display:
- Props: `error: unknown`, `onRetry?: () => void`
- Extracts `ApiError.detail` or falls back to "An unexpected error occurred"
- Shows Retry button when `onRetry` provided

**`shared/components/feedback/EmptyState.tsx`** — empty table display:
- Props: `message?: string` (default "No data found")

---

## DTO Types

### `shared/types/dashboard.ts`
```typescript
export interface DashboardSummaryDto {
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  pendingEvents: number;
  queueDepth: number;
  eventsToday: number;
  transportErrors24h: number;
  generatedAt: string;
}

export interface ActivityItemDto {
  // exact fields from GET /dashboard/activity
  activityId: number;
  type: string;
  description: string;
  nodeId?: string;
  createTime: string;
}
```

### `shared/types/events.ts`
```typescript
export interface EventSummaryDto {
  eventId: number;
  triggerId: string;
  sourceNodeId: string;
  channelId: string;
  eventType: string;
  tableName: string;
  batchId?: number;
  createTime: string;
  isProcessed: boolean;
}

export interface EventDetailDto extends EventSummaryDto {
  pkData: string;
  rowData: string;
  transactionId?: string;
}

export interface EventFilter {
  sourceNodeId?: string;
  triggerId?: string;
  channelId?: string;
  eventType?: string;
  isProcessed?: boolean;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}
```

### `shared/types/batches.ts`
```typescript
export interface IncomingBatchSummaryDto {
  batchId: number;
  sourceNodeId: string;
  channelId: string;
  status: string;
  rowCount: number;
  receivedTime: string;
  appliedTime?: string;
  applyTimeMs?: number;
}

export interface OutgoingBatchDto {
  batchId: number;
  status: string;
  nodeId: string;
  channelId: string;
  createTime: string;
  sentTime?: string;
  ackTime?: string;
  retryCount: number;
  rowCount: number;
  error?: string;
}

export interface BatchErrorDetailDto {
  errorId: number;
  batchId: number;
  conflictType: string;
  severity: string;
  createTime: string;
  detail?: string;
}

export interface IncomingBatchFilter {
  sourceNodeId?: string;
  channelId?: string;
  status?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}

export interface OutgoingBatchFilter {
  status?: string;
  nodeId?: string;
  channelId?: string;
  sortBy?: 'createTime' | 'batchId' | 'status';
  sortDirection?: 'asc' | 'desc';
  page: number;
  pageSize: number;
}

export interface BatchErrorFilter {
  batchId?: number;
  conflictType?: string;
  severity?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}
```

### `shared/types/nodes.ts`
```typescript
export interface NodeDto {
  nodeId: string;
  groupId: string;
  name: string;
  status: string;
  syncEnabled: boolean;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
  createdTime: string;
}
```

### `shared/types/topology.ts`
```typescript
export interface TopologySummaryDto {
  totalGroups: number;
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  generatedAt: string;
}

export interface TopologyGroupDto {
  groupId: string;
  name: string;
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  connectivityStatus: string;
}

export interface TopologyGroupNodeDto {
  nodeId: string;
  name: string;
  status: string;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
}
```

### `shared/types/metrics.ts`
```typescript
export interface MetricsSummaryDto {
  totalNodes: number;
  reachableNodes: number;
  degradedNodes: number;
  unreachableNodes: number;
  unknownNodes: number;
  incomingQueueDepth: number;
  outgoingQueueDepth: number;
  batchesProcessed24h: number;
  errors24h: number;
  errorRatePercent: number;
  throughputPerMinute: number;
  generatedAt: string;
}

export interface NodeMetricsDto {
  nodeId: string;
  name: string;
  status: string;
  pendingEvents: number;
  batchesSent: number;
  batchesReceived: number;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
}

export interface ChannelMetricsDto {
  channelId: string;
  name: string;
  queueDepth: number;
  throughputPerMinute: number;
  errorRate: number;
}

export interface RuntimeMetricsDto {
  uptimeSeconds: number;
  memoryMb: number;
  cpuPercent: number;
  activeWorkers: number;
  generatedAt: string;
}
```

### `shared/types/channels.ts`
```typescript
export interface ChannelDto {
  channelId: string;
  name: string;
  description?: string;
  enabled: boolean;
  createdTime: string;
}
```

### `shared/types/triggers.ts`
```typescript
export interface TriggerDto {
  triggerId: string;
  channelId: string;
  tableName: string;
  schemaName: string;
  captureInsert: boolean;
  captureUpdate: boolean;
  captureDelete: boolean;
  enabled: boolean;
  createdTime: string;
}
```

### `shared/types/routers.ts`
```typescript
export interface RouterDto {
  routerId: string;
  name: string;
  sourceGroupId: string;
  targetGroupId: string;
  channelIds: string[];
  enabled: boolean;
  createdTime: string;
}
```

### `shared/types/users.ts`
```typescript
export interface UserSummaryDto {
  userId: number;
  username: string;
  enabled: boolean;
  roles: string[];
  createdTime: string;
  lastLoginTime?: string;
}

export interface UserFilter {
  page: number;
  pageSize: number;
  enabled?: boolean;
  search?: string;
}
```

### `shared/types/parameters.ts`
```typescript
export interface ParameterDto {
  name: string;
  value: string;
  isSecret: boolean;
  updatedTime?: string;
}

export interface ParameterDescriptorDto {
  name: string;
  description: string;
  isSecret: boolean;
  requiresRestart: boolean;
  isDynamic: boolean;
}
```

### `shared/types/audit.ts`
```typescript
export interface AuditDto {
  auditId: number;
  username: string;
  actionName: string;
  objectName?: string;
  correlationId?: string;
  createTime: string;
}

export interface AuditFilter {
  username?: string;
  actionName?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}
```

### `shared/types/locks.ts`
```typescript
export interface LockDto {
  lockName: string;
  lockOwner: string;
  lockTime: string;
}
```

---

## Utility Functions

### `shared/utils/date.ts`
```typescript
export function formatDateTime(iso: string): string
// "2026-06-29 14:32:01" (local time, fixed format)

export function formatRelativeTime(iso: string): string
// "12 seconds ago", "3 minutes ago", "2 hours ago", "Jun 29"
```

### `shared/utils/numbers.ts`
```typescript
export function formatLatency(ms: number): string
// "< 1ms", "42ms", "1.2s"

export function formatQueueDepth(count: number): string
// "0", "1,234", "10k+"

export function formatPercent(value: number): string
// "12.3%"

export function formatUptime(seconds: number): string
// "2d 14h 32m"
```

### `shared/utils/status.ts`
```typescript
export type StatusVariant = 'success' | 'warning' | 'danger' | 'neutral';

export function nodeStatusVariant(status: string): StatusVariant
// REGISTERED→success, DEGRADED→warning, OFFLINE/UNREACHABLE→danger, else neutral

export function batchStatusVariant(status: string): StatusVariant
// OK→success, SENT→neutral, ERROR/CONFLICT→danger, LOADING→warning

export function connectivityStatusVariant(status: string): StatusVariant
// HEALTHY→success, DEGRADED→warning, UNREACHABLE→danger
```

---

## Page Designs

### Dashboard (`/dashboard`)

API calls:
- `GET /api/v1/dashboard/summary` — 30s polling
- `GET /api/v1/dashboard/activity?page=1&pageSize=20`

Layout:
```
Row 1: SummaryCards (6 cards)
  Total Nodes | Reachable | Degraded | Unreachable | Events Today | Queue Depth

Row 2: Activity Feed (last 20 items, client-side paginated)
  Timestamp | Type | Description | Node
```

`SummaryCards` uses `refetchInterval: DASHBOARD_REFRESH_MS`, `refetchOnWindowFocus: true`.
Activity feed uses `staleTime: 30_000` with no auto-refresh.
Last-updated timestamp displayed below summary cards: "Updated 12 seconds ago" (computed from `generatedAt`).

---

### Events (`/events`) — server-side

API: `GET /api/v1/events` — `PagedResult<EventSummaryDto>`

Filters (above grid): sourceNodeId (text), eventType (select: INSERT/UPDATE/DELETE), isProcessed (select: All/Pending/Processed), from/to (date inputs)

Columns: Event ID | Trigger ID | Source Node | Channel | Type | Table | Batch ID | Created | Processed

Default: page=1, pageSize=50, no filters.

---

### Incoming Batches (`/incoming-batches`) — server-side

API: `GET /api/v1/incoming-batches` — `PagedResult<IncomingBatchSummaryDto>`

Filters: sourceNodeId, channelId, status (select), from/to

Columns: Batch ID | Source Node | Channel | Status (StatusBadge) | Rows | Received | Applied | Apply Time

---

### Outgoing Batches (`/outgoing-batches`) — server-side

API: `GET /api/v1/batches` — `{ data: OutgoingBatchDto[], total, page, pageSize, totalPages }`

Filters: nodeId, channelId, status (select)

Columns: Batch ID | Node | Channel | Status (StatusBadge) | Rows | Retries | Created | Sent | Ack | Error (truncated)

---

### Batch Errors (`/batch-errors`) — server-side

API: `GET /api/v1/batch-errors` — `PagedResult<BatchErrorDetailDto>`

Filters: batchId (number), conflictType (select), severity (select), from/to

Columns: Error ID | Batch ID | Conflict Type | Severity (StatusBadge) | Created | Detail (truncated)

---

### Nodes (`/nodes`) — client-side

API: `GET /api/v1/nodes` — `NodeDto[]`

No filters (AG Grid quick search). `staleTime: 60_000`.

Columns: Node ID | Group | Name | Status (StatusBadge) | Sync Enabled | Last Heartbeat (relative) | Probe Latency | Created

---

### Topology (`/topology`) — client-side

API calls:
- `GET /api/v1/topology/summary` — summary cards
- `GET /api/v1/topology/groups` — groups grid

Layout:
```
Row 1: SummaryCards (4 cards)
  Total Groups | Total Nodes | Reachable | Unreachable

Row 2: TopologyGroupsGrid (client-side)
  Group ID | Name | Total Nodes | Reachable | Degraded | Unreachable | Status (StatusBadge)
```

React Flow graph deferred to Epic 10C.

---

### Metrics (`/metrics`) — 30s polling

API calls:
- `GET /api/v1/metrics/summary` — polling
- `GET /api/v1/metrics/nodes` — client-side grid
- `GET /api/v1/metrics/channels` — client-side grid
- `GET /api/v1/metrics/runtime` — single summary row

Layout:
```
Row 1: SummaryCards (6 cards) — auto-refresh 30s
  Queue Depth In | Queue Depth Out | Batches 24h | Errors 24h | Error Rate | Throughput/min

Row 2: Runtime stats (uptime, memory, CPU, workers) — single row

Row 3: NodeMetricsGrid (client-side)

Row 4: ChannelMetricsGrid (client-side)
```

ApexCharts charts deferred to Epic 10C.

---

### Channels (`/channels`) — client-side

API: `GET /api/v1/channels` — `ChannelDto[]`

Columns: Channel ID | Name | Description | Enabled (badge) | Created

---

### Triggers (`/triggers`) — client-side

API: `GET /api/v1/triggers` — `TriggerDto[]`

Columns: Trigger ID | Channel | Schema | Table | Insert | Update | Delete | Enabled (badge) | Created

---

### Routers (`/routers`) — client-side

API: `GET /api/v1/routers` — `RouterDto[]`

Columns: Router ID | Name | Source Group | Target Group | Channels | Enabled (badge) | Created

---

### Users (`/users`) — client-side (admin only)

API: `GET /api/v1/users` — `PagedResult<UserSummaryDto>` but total expected < 100 in practice; fetch with `pageSize=200` to load all client-side.

Columns: User ID | Username | Roles | Enabled (badge) | Created | Last Login (relative)

Quick search over username + roles.

---

### Parameters (`/parameters`) — client-side

API calls:
- `GET /api/v1/parameters` — `ParameterDto[]`
- `GET /api/v1/parameters/descriptors` — `ParameterDescriptorDto[]` (joined in-memory by name)

Columns: Name | Value (mask if `isSecret`) | Description | Secret | Requires Restart | Dynamic | Updated

Secret parameter values display as `••••••••` regardless of actual value.

---

### Audit (`/audit`) — server-side

API: `GET /api/v1/audit` — `PagedResult<AuditDto>`

Filters: username (text), actionName (text), from/to (date inputs)

Columns: Audit ID | Username | Action | Object | Correlation ID | Created

Default: page=1, pageSize=50, no filters.

---

### Locks (`/locks`) — client-side

API: `GET /api/v1/locks` — `LockDto[]`

No filters. `staleTime: 30_000`.

Columns: Lock Name | Owner | Held Since (relative) | Duration (computed from lockTime)

Lock release (DELETE) deferred to Epic 10C.

---

### Profile (`/profile`) — no API call

Reads user info from `localStorage.getItem('msosync.user')` (set by AuthProvider on login/refresh).

Displays: Username, Roles, Token expiry countdown.

No React Query. No API call.

---

## Router Update

Add `/locks` to the router tree under `AuthGuard → AppLayout`. Update the sidebar nav under Administration group: add "Locks" entry.

Update `src/MSOSync.Frontend/src/app/router.tsx` and `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`.

---

## AG Grid Theme

Use `ag-theme-quartz` class on all AG Grid instances. Import AG Grid CSS once in `src/index.css`:
```css
@import "ag-grid-community/styles/ag-grid.css";
@import "ag-grid-community/styles/ag-theme-quartz.css";
```

---

## Testing

No new Vitest unit tests required for this epic (data fetching components are best integration-tested). The 12 existing tests must continue to pass after every task (`npm test`).

---

## Acceptance Criteria

```
□ 1.  Dashboard shows live summary cards and activity feed; auto-refreshes every 30s
□ 2.  Events grid loads with server-side pagination and filter controls
□ 3.  Incoming Batches grid loads with server-side pagination and filter controls
□ 4.  Outgoing Batches grid loads with server-side pagination and filter controls
□ 5.  Batch Errors grid loads with server-side pagination and filter controls
□ 6.  Audit grid loads with server-side pagination and filter controls
□ 7.  Nodes grid loads all nodes (client-side) with quick search
□ 8.  Topology page shows summary cards and groups grid
□ 9.  Metrics page shows summary cards and node/channel grids; auto-refreshes every 30s
□ 10. Channels, Triggers, Routers grids load all records (client-side)
□ 11. Users grid loads (client-side)
□ 12. Parameters grid loads with secret masking
□ 13. Locks grid loads (client-side)
□ 14. Profile page shows username, roles, and token expiry
□ 15. All pages show loading, empty, and error states
□ 16. No PlaceholderPage components remain (except Topology — graph deferred to 10C)
□ 17. npm run build exits 0, npm run lint exits 0, npm test 12/12 pass
□ 18. AG Grid theme consistent across all pages (ag-theme-quartz)
□ 19. All query keys come from shared/queryKeys.ts
□ 20. No mock/static data anywhere
```
