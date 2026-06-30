# Epic 10B — Task 1: Shared Types, Constants, QueryKeys, Utils, API Functions

> Read the master plan first: `docs/superpowers/plans/2026-06-30-epic10b-data-tables.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

**Goal:** Create all TypeScript-only shared infrastructure: DTO types, constants, query key factory, utility functions, and API fetch functions. No React in this task.

**Files to create** (all under `src/MSOSync.Frontend/src/`):

Create:
- `shared/types/common.ts`
- `shared/types/dashboard.ts`
- `shared/types/events.ts`
- `shared/types/batches.ts`
- `shared/types/nodes.ts`
- `shared/types/topology.ts`
- `shared/types/metrics.ts`
- `shared/types/channels.ts`
- `shared/types/triggers.ts`
- `shared/types/routers.ts`
- `shared/types/users.ts`
- `shared/types/parameters.ts`
- `shared/types/audit.ts`
- `shared/types/locks.ts`
- `shared/types/index.ts`
- `shared/constants/query.ts`
- `shared/queryKeys.ts`
- `shared/utils/date.ts`
- `shared/utils/numbers.ts`
- `shared/utils/status.ts`
- `shared/api/dashboard.ts`
- `shared/api/events.ts`
- `shared/api/batches.ts`
- `shared/api/nodes.ts`
- `shared/api/topology.ts`
- `shared/api/metrics.ts`
- `shared/api/channels.ts`
- `shared/api/triggers.ts`
- `shared/api/routers.ts`
- `shared/api/users.ts`
- `shared/api/parameters.ts`
- `shared/api/audit.ts`
- `shared/api/locks.ts`

**Interfaces — Consumes:**
- `src/MSOSync.Frontend/src/shared/api/client.ts` — `export default client` (Axios instance, baseURL `/api/v1`)
- `src/MSOSync.Frontend/src/shared/types/auth.ts` — existing, do not modify

**Interfaces — Produces** (later tasks import from these paths):
- `import type { PagedResult } from '../../shared/types/common'`
- `import type { ... } from '../../shared/types'` (index re-exports all)
- `import { queryKeys } from '../../shared/queryKeys'`
- `import { DASHBOARD_REFRESH_MS } from '../../shared/constants/query'`
- `import { formatDateTime, formatRelativeTime } from '../../shared/utils/date'`
- `import { formatLatency, formatQueueDepth, formatPercent, formatUptime } from '../../shared/utils/numbers'`
- `import { nodeStatusVariant, batchStatusVariant, connectivityStatusVariant } from '../../shared/utils/status'`
- `import { getDashboardSummary, getDashboardActivity } from '../../shared/api/dashboard'`
- (similar imports for all other api files)

---

- [ ] **Step 1: Create `shared/types/common.ts`**

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

- [ ] **Step 2: Create `shared/types/dashboard.ts`**

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
  activityId: number;
  type: string;
  description: string;
  nodeId?: string;
  createTime: string;
}
```

- [ ] **Step 3: Create `shared/types/events.ts`**

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

- [ ] **Step 4: Create `shared/types/batches.ts`**

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

- [ ] **Step 5: Create `shared/types/nodes.ts`**

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

- [ ] **Step 6: Create `shared/types/topology.ts`**

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

- [ ] **Step 7: Create `shared/types/metrics.ts`**

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

- [ ] **Step 8: Create `shared/types/channels.ts`, `triggers.ts`, `routers.ts`**

`shared/types/channels.ts`:
```typescript
export interface ChannelDto {
  channelId: string;
  name: string;
  description?: string;
  enabled: boolean;
  createdTime: string;
}
```

`shared/types/triggers.ts`:
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

`shared/types/routers.ts`:
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

- [ ] **Step 9: Create `shared/types/users.ts`, `parameters.ts`, `audit.ts`, `locks.ts`**

`shared/types/users.ts`:
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

`shared/types/parameters.ts`:
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

`shared/types/audit.ts`:
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

`shared/types/locks.ts`:
```typescript
export interface LockDto {
  lockName: string;
  lockOwner: string;
  lockTime: string;
}
```

- [ ] **Step 10: Create `shared/types/index.ts`**

```typescript
export * from './common';
export * from './dashboard';
export * from './events';
export * from './batches';
export * from './nodes';
export * from './topology';
export * from './metrics';
export * from './channels';
export * from './triggers';
export * from './routers';
export * from './users';
export * from './parameters';
export * from './audit';
export * from './locks';
```

- [ ] **Step 11: Create `shared/constants/query.ts`**

```typescript
export const DASHBOARD_REFRESH_MS = 30_000;
export const DEFAULT_PAGE_SIZE = 50;
export const DEFAULT_BATCH_PAGE_SIZE = 20;
```

- [ ] **Step 12: Create `shared/queryKeys.ts`**

```typescript
import type {
  EventFilter,
  IncomingBatchFilter,
  OutgoingBatchFilter,
  BatchErrorFilter,
  AuditFilter,
  UserFilter,
} from './types';

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

- [ ] **Step 13: Create `shared/utils/date.ts`**

```typescript
export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

export function formatRelativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime();
  const diffSec = Math.round(diffMs / 1000);
  if (diffSec < 60) return `${diffSec} seconds ago`;
  const diffMin = Math.round(diffSec / 60);
  if (diffMin < 60) return `${diffMin} minutes ago`;
  const diffHr = Math.round(diffMin / 60);
  if (diffHr < 24) return `${diffHr} hours ago`;
  const d = new Date(iso);
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}
```

- [ ] **Step 14: Create `shared/utils/numbers.ts`**

```typescript
export function formatLatency(ms: number): string {
  if (ms < 1) return '< 1ms';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

export function formatQueueDepth(count: number): string {
  if (count === 0) return '0';
  if (count >= 10000) return '10k+';
  return count.toLocaleString();
}

export function formatPercent(value: number): string {
  return `${value.toFixed(1)}%`;
}

export function formatUptime(seconds: number): string {
  const d = Math.floor(seconds / 86400);
  const h = Math.floor((seconds % 86400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const parts: string[] = [];
  if (d > 0) parts.push(`${d}d`);
  if (h > 0) parts.push(`${h}h`);
  parts.push(`${m}m`);
  return parts.join(' ');
}
```

- [ ] **Step 15: Create `shared/utils/status.ts`**

```typescript
export type StatusVariant = 'success' | 'warning' | 'danger' | 'neutral';

export function nodeStatusVariant(status: string): StatusVariant {
  switch (status.toUpperCase()) {
    case 'REGISTERED': return 'success';
    case 'DEGRADED': return 'warning';
    case 'OFFLINE':
    case 'UNREACHABLE': return 'danger';
    default: return 'neutral';
  }
}

export function batchStatusVariant(status: string): StatusVariant {
  switch (status.toUpperCase()) {
    case 'OK': return 'success';
    case 'SENT': return 'neutral';
    case 'ERROR':
    case 'CONFLICT': return 'danger';
    case 'LOADING': return 'warning';
    default: return 'neutral';
  }
}

export function connectivityStatusVariant(status: string): StatusVariant {
  switch (status.toUpperCase()) {
    case 'HEALTHY': return 'success';
    case 'DEGRADED': return 'warning';
    case 'UNREACHABLE': return 'danger';
    default: return 'neutral';
  }
}
```

- [ ] **Step 16: Create API files — `shared/api/dashboard.ts` and `shared/api/events.ts`**

`shared/api/dashboard.ts`:
```typescript
import client from './client';
import type { DashboardSummaryDto, ActivityItemDto } from '../types';
import type { PagedResult } from '../types/common';

export async function getDashboardSummary(): Promise<DashboardSummaryDto> {
  const { data } = await client.get<DashboardSummaryDto>('/dashboard/summary');
  return data;
}

export async function getDashboardActivity(
  page = 1,
  pageSize = 20,
): Promise<PagedResult<ActivityItemDto>> {
  const { data } = await client.get<PagedResult<ActivityItemDto>>('/dashboard/activity', {
    params: { page, pageSize },
  });
  return data;
}
```

`shared/api/events.ts`:
```typescript
import client from './client';
import type { EventSummaryDto, EventFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getEvents(filter: EventFilter): Promise<PagedResult<EventSummaryDto>> {
  const { data } = await client.get<PagedResult<EventSummaryDto>>('/events', {
    params: filter,
  });
  return data;
}
```

- [ ] **Step 17: Create `shared/api/batches.ts`**

```typescript
import client from './client';
import type {
  IncomingBatchSummaryDto,
  OutgoingBatchDto,
  BatchErrorDetailDto,
  IncomingBatchFilter,
  OutgoingBatchFilter,
  BatchErrorFilter,
} from '../types';
import type { PagedResult } from '../types/common';

export async function getIncomingBatches(
  filter: IncomingBatchFilter,
): Promise<PagedResult<IncomingBatchSummaryDto>> {
  const { data } = await client.get<PagedResult<IncomingBatchSummaryDto>>('/incoming-batches', {
    params: filter,
  });
  return data;
}

export async function getOutgoingBatches(
  filter: OutgoingBatchFilter,
): Promise<PagedResult<OutgoingBatchDto>> {
  const { data } = await client.get<PagedResult<OutgoingBatchDto>>('/batches', {
    params: filter,
  });
  return data;
}

export async function getBatchErrors(
  filter: BatchErrorFilter,
): Promise<PagedResult<BatchErrorDetailDto>> {
  const { data } = await client.get<PagedResult<BatchErrorDetailDto>>('/batch-errors', {
    params: filter,
  });
  return data;
}
```

- [ ] **Step 18: Create remaining API files**

`shared/api/nodes.ts`:
```typescript
import client from './client';
import type { NodeDto } from '../types';

export async function getNodes(): Promise<NodeDto[]> {
  const { data } = await client.get<NodeDto[]>('/nodes');
  return data;
}
```

`shared/api/topology.ts`:
```typescript
import client from './client';
import type { TopologySummaryDto, TopologyGroupDto, TopologyGroupNodeDto } from '../types';

export async function getTopologySummary(): Promise<TopologySummaryDto> {
  const { data } = await client.get<TopologySummaryDto>('/topology/summary');
  return data;
}

export async function getTopologyGroups(): Promise<TopologyGroupDto[]> {
  const { data } = await client.get<TopologyGroupDto[]>('/topology/groups');
  return data;
}

export async function getTopologyGroupNodes(groupId: string): Promise<TopologyGroupNodeDto[]> {
  const { data } = await client.get<TopologyGroupNodeDto[]>(`/topology/groups/${groupId}/nodes`);
  return data;
}
```

`shared/api/metrics.ts`:
```typescript
import client from './client';
import type {
  MetricsSummaryDto,
  NodeMetricsDto,
  ChannelMetricsDto,
  RuntimeMetricsDto,
} from '../types';

export async function getMetricsSummary(): Promise<MetricsSummaryDto> {
  const { data } = await client.get<MetricsSummaryDto>('/metrics/summary');
  return data;
}

export async function getNodeMetrics(): Promise<NodeMetricsDto[]> {
  const { data } = await client.get<NodeMetricsDto[]>('/metrics/nodes');
  return data;
}

export async function getChannelMetrics(): Promise<ChannelMetricsDto[]> {
  const { data } = await client.get<ChannelMetricsDto[]>('/metrics/channels');
  return data;
}

export async function getRuntimeMetrics(): Promise<RuntimeMetricsDto> {
  const { data } = await client.get<RuntimeMetricsDto>('/metrics/runtime');
  return data;
}
```

`shared/api/channels.ts`:
```typescript
import client from './client';
import type { ChannelDto } from '../types';

export async function getChannels(): Promise<ChannelDto[]> {
  const { data } = await client.get<ChannelDto[]>('/channels');
  return data;
}
```

`shared/api/triggers.ts`:
```typescript
import client from './client';
import type { TriggerDto } from '../types';

export async function getTriggers(): Promise<TriggerDto[]> {
  const { data } = await client.get<TriggerDto[]>('/triggers');
  return data;
}
```

`shared/api/routers.ts`:
```typescript
import client from './client';
import type { RouterDto } from '../types';

export async function getRouters(): Promise<RouterDto[]> {
  const { data } = await client.get<RouterDto[]>('/routers');
  return data;
}
```

`shared/api/users.ts`:
```typescript
import client from './client';
import type { UserSummaryDto, UserFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getUsers(filter: UserFilter): Promise<PagedResult<UserSummaryDto>> {
  const { data } = await client.get<PagedResult<UserSummaryDto>>('/users', {
    params: filter,
  });
  return data;
}
```

`shared/api/parameters.ts`:
```typescript
import client from './client';
import type { ParameterDto, ParameterDescriptorDto } from '../types';

export async function getParameters(): Promise<ParameterDto[]> {
  const { data } = await client.get<ParameterDto[]>('/parameters');
  return data;
}

export async function getParameterDescriptors(): Promise<ParameterDescriptorDto[]> {
  const { data } = await client.get<ParameterDescriptorDto[]>('/parameters/descriptors');
  return data;
}
```

`shared/api/audit.ts`:
```typescript
import client from './client';
import type { AuditDto, AuditFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getAuditLog(filter: AuditFilter): Promise<PagedResult<AuditDto>> {
  const { data } = await client.get<PagedResult<AuditDto>>('/audit', {
    params: filter,
  });
  return data;
}
```

`shared/api/locks.ts`:
```typescript
import client from './client';
import type { LockDto } from '../types';

export async function getLocks(): Promise<LockDto[]> {
  const { data } = await client.get<LockDto[]>('/locks');
  return data;
}
```

- [ ] **Step 19: Verify build + lint + tests pass**

Run from `src/MSOSync.Frontend/`:
```
npm run build
```
Expected: exits 0, no TypeScript errors.

```
npm run lint
```
Expected: 0 errors, 0 warnings.

```
npm test
```
Expected: 12 tests pass (existing tests — no new tests in this task).

- [ ] **Step 20: Commit**

```
git add src/MSOSync.Frontend/src/shared/types/
git add src/MSOSync.Frontend/src/shared/constants/
git add src/MSOSync.Frontend/src/shared/queryKeys.ts
git add src/MSOSync.Frontend/src/shared/utils/
git add src/MSOSync.Frontend/src/shared/api/dashboard.ts
git add src/MSOSync.Frontend/src/shared/api/events.ts
git add src/MSOSync.Frontend/src/shared/api/batches.ts
git add src/MSOSync.Frontend/src/shared/api/nodes.ts
git add src/MSOSync.Frontend/src/shared/api/topology.ts
git add src/MSOSync.Frontend/src/shared/api/metrics.ts
git add src/MSOSync.Frontend/src/shared/api/channels.ts
git add src/MSOSync.Frontend/src/shared/api/triggers.ts
git add src/MSOSync.Frontend/src/shared/api/routers.ts
git add src/MSOSync.Frontend/src/shared/api/users.ts
git add src/MSOSync.Frontend/src/shared/api/parameters.ts
git add src/MSOSync.Frontend/src/shared/api/audit.ts
git add src/MSOSync.Frontend/src/shared/api/locks.ts
git commit -m "feat(10b): add shared types, utils, constants, queryKeys, API functions"
```

---

## Report Contract

Write report to the path specified by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files created (count)
- Build result
- Lint result
- Test result (N/12 pass)
- Any concerns
