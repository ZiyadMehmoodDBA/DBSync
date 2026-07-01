# Epic 11C Task 3: Event Router + Notifications + Polling Policy

## Context

You are wiring event routing and toast notifications into the SignalR provider, and updating polling policies across all feature hooks. Tasks 1 and 2 are complete: the hub publishes `"OperationsEvent"` messages and `SignalRProvider.tsx` has a stub `handleEvent` callback.

**Key facts about the existing codebase:**
- `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx` has `handleEvent` stubbed as `(_event: OperationsEvent) => {}` — Task 3 replaces this stub with real routing calls
- `queryKeys.ts` has these keys (exact string values): `['dashboard-summary']`, `['events', filter]`, `['incoming-batches', filter]`, `['outgoing-batches', filter]`, `['batch-errors', filter]`, `['nodes']`, `['topology-graph']`, `['topology-summary']`, `['topology-groups']`, `['metrics-summary']`
- For parameterized keys (`events`, `incoming-batches`, etc.), invalidate by the first segment only: `{ queryKey: ['events'] }` invalidates all pages
- `DASHBOARD_REFRESH_MS = 30_000` constant lives in `src/shared/constants/query.ts` — change it to `300_000`
- `dashboard/hooks.ts` uses `DASHBOARD_REFRESH_MS` for `refetchInterval` and `refetchIntervalInBackground: false`
- `metrics/hooks.ts` uses `DASHBOARD_REFRESH_MS` for `useMetricsSummary` and `useRuntimeMetrics`
- All historical hooks (`events`, `incoming-batches`, `outgoing-batches`, `batch-errors`) currently have `refetchOnWindowFocus: false` — change to `true`
- All metadata hooks (`users`, `channels`, `triggers`, `routers`, `parameters`) currently have `staleTime: 60_000` and `refetchOnWindowFocus: false` — add `staleTime: Infinity` (keeping `refetchOnWindowFocus: false`)
- Relative imports ONLY in `shared/signalr/` — no `@/` aliases

## Interfaces

**Consumes (from Task 2):**
- `OperationsEvent`, `OperationsEventType` from `./types`
- `SignalRProvider.tsx` — will modify the `handleEvent` stub

**Consumes (already in codebase):**
- `QueryClient.invalidateQueries({ queryKey })` from `@tanstack/react-query`
- `toast` from `sonner`

**Produces:**
- `routeToCache(queryClient, event): Promise<void>` — consumed by Task 4 (already wired in SignalRProvider)
- `routeToToast(event): void` — consumed by Task 4 (already wired in SignalRProvider)

---

## Files

- Create: `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`
- Create: `src/MSOSync.Frontend/src/shared/signalr/notifications.ts`
- Modify: `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx` (wire routing)
- Create: `src/MSOSync.Frontend/src/shared/signalr/eventRouter.test.ts`
- Create: `src/MSOSync.Frontend/src/shared/signalr/notifications.test.ts`
- Modify: `src/MSOSync.Frontend/src/shared/constants/query.ts`
- Modify: `src/MSOSync.Frontend/src/features/dashboard/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/metrics/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/events/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/incoming-batches/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/outgoing-batches/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/batch-errors/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/users/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/channels/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/triggers/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/routers/hooks.ts`
- Modify: `src/MSOSync.Frontend/src/features/parameters/hooks.ts`

---

- [ ] **Step 1: Create `eventRouter.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`:

```ts
import type { QueryClient } from '@tanstack/react-query';
import { OperationsEventType, type OperationsEvent } from './types';

export async function routeToCache(
  queryClient: QueryClient,
  event: OperationsEvent,
): Promise<void> {
  switch (event.type) {
    case OperationsEventType.NodeHealthChanged:
      return invalidateNodeHealth(queryClient);
    case OperationsEventType.NodeApproved:
    case OperationsEventType.NodeRejected:
    case OperationsEventType.NodeDisabled:
    case OperationsEventType.NodeEnabled:
      return invalidateNodeLifecycle(queryClient);
    case OperationsEventType.SyncCycleCompleted:
      return invalidateOperational(queryClient);
  }
}

async function invalidateNodeHealth(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['nodes'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['metrics-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
  ]);
}

async function invalidateNodeLifecycle(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['nodes'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-groups'] }),
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['metrics-summary'] }),
  ]);
}

async function invalidateOperational(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['events'] }),
    queryClient.invalidateQueries({ queryKey: ['incoming-batches'] }),
    queryClient.invalidateQueries({ queryKey: ['outgoing-batches'] }),
    queryClient.invalidateQueries({ queryKey: ['batch-errors'] }),
    queryClient.invalidateQueries({ queryKey: ['metrics-summary'] }),
  ]);
}
```

- [ ] **Step 2: Create `notifications.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/notifications.ts`:

```ts
import { toast } from 'sonner';
import { OperationsEventType, type OperationsEvent } from './types';

const seen = new Map<string, true>();

export function routeToToast(event: OperationsEvent): void {
  const label = event.nodeLabel ?? event.nodeId;

  switch (event.type) {
    case OperationsEventType.NodeHealthChanged: {
      const message = resolveHealthMessage(label, event.previousStatus, event.currentStatus);
      if (message) showDeduped(event, message.text, message.severity);
      break;
    }
    case OperationsEventType.NodeApproved:
      showDeduped(event, `Node ${label} approved.`, 'success');
      break;
    case OperationsEventType.NodeRejected:
      showDeduped(event, `Node ${label} registration rejected.`, 'warning');
      break;
    case OperationsEventType.NodeDisabled:
      showDeduped(event, `Node ${label} disabled.`, 'warning');
      break;
    case OperationsEventType.NodeEnabled:
      showDeduped(event, `Node ${label} re-enabled.`, 'info');
      break;
    case OperationsEventType.SyncCycleCompleted:
      // Silent cache invalidation — no toast
      break;
  }
}

function resolveHealthMessage(
  label: string,
  previousStatus: string | null,
  currentStatus: string | null,
): { text: string; severity: 'success' | 'warning' | 'error' } | null {
  if (currentStatus === 'Reachable') {
    return { text: `Node ${label} is reachable again.`, severity: 'success' };
  }
  if (currentStatus === 'Degraded') {
    return { text: `Node ${label} is degraded.`, severity: 'warning' };
  }
  if (currentStatus === 'Unreachable' && previousStatus !== 'Unreachable') {
    return { text: `Node ${label} is unreachable.`, severity: 'error' };
  }
  return null;
}

function showDeduped(
  event: OperationsEvent,
  message: string,
  severity: 'success' | 'warning' | 'error' | 'info',
): void {
  const bucket = Math.floor(new Date(event.occurredAt).getTime() / 30_000);
  const key    = `${event.type}:${event.nodeId}:${event.currentStatus}:${bucket}`;

  if (seen.has(key)) return;

  if (seen.size > 1000) seen.clear();
  seen.set(key, true);

  toast[severity](message);
}
```

- [ ] **Step 3: Update `SignalRProvider.tsx` to wire routing**

Open `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx`. Replace the stub `handleEvent` with real routing:

Current:
```tsx
import { useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../features/auth/useAuth';
import { SignalRContext } from './context';
import { useSignalR } from './useSignalR';
import type { OperationsEvent } from './types';

interface Props {
  children: ReactNode;
}

export function SignalRProvider({ children }: Props) {
  const { accessToken } = useAuth();
  const queryClient = useQueryClient();

  const getAccessToken = useCallback(() => accessToken, [accessToken]);

  const handleEvent = useCallback((_event: OperationsEvent) => {
    // Routing wired in Task 3 — placeholder keeps the callback stable
  }, []);

  const { connectionState, lastConnectedAt, lastDisconnectedAt } = useSignalR({
    getAccessToken,
    isAuthenticated: accessToken !== null,
    queryClient,
    onEvent: handleEvent,
  });

  return (
    <SignalRContext.Provider value={{ connectionState, lastConnectedAt, lastDisconnectedAt }}>
      {children}
    </SignalRContext.Provider>
  );
}
```

Replace with:

```tsx
import { useCallback, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useAuth } from '../../features/auth/useAuth';
import { SignalRContext } from './context';
import { useSignalR } from './useSignalR';
import { routeToCache } from './eventRouter';
import { routeToToast } from './notifications';
import type { OperationsEvent } from './types';

interface Props {
  children: ReactNode;
}

export function SignalRProvider({ children }: Props) {
  const { accessToken } = useAuth();
  const queryClient = useQueryClient();

  const getAccessToken = useCallback(() => accessToken, [accessToken]);

  const handleEvent = useCallback(
    (event: OperationsEvent) => {
      void routeToCache(queryClient, event);
      routeToToast(event);
    },
    [queryClient],
  );

  const { connectionState, lastConnectedAt, lastDisconnectedAt } = useSignalR({
    getAccessToken,
    isAuthenticated: accessToken !== null,
    queryClient,
    onEvent: handleEvent,
  });

  return (
    <SignalRContext.Provider value={{ connectionState, lastConnectedAt, lastDisconnectedAt }}>
      {children}
    </SignalRContext.Provider>
  );
}
```

- [ ] **Step 4: Create `eventRouter.test.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/eventRouter.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { routeToCache } from './eventRouter';
import { OperationsEventType, type OperationsEvent } from './types';

function makeEvent(type: OperationsEventType): OperationsEvent {
  return {
    type,
    nodeId: 'node-1',
    nodeLabel: null,
    previousStatus: null,
    currentStatus: null,
    occurredAt: new Date().toISOString(),
    groupId: null,
  };
}

describe('routeToCache', () => {
  let queryClient: QueryClient;
  let invalidateSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    queryClient = new QueryClient();
    invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
  });

  it('NodeHealthChanged invalidates nodes, topology-graph, topology-summary, metrics-summary, dashboard-summary', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeHealthChanged));

    const keys = invalidateSpy.mock.calls.map((c) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toContain('nodes');
    expect(keys).toContain('topology-graph');
    expect(keys).toContain('topology-summary');
    expect(keys).toContain('metrics-summary');
    expect(keys).toContain('dashboard-summary');
    expect(keys).toHaveLength(5);
  });

  it('NodeApproved invalidates nodes, topology-graph, topology-groups, dashboard-summary, metrics-summary', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeApproved));

    const keys = invalidateSpy.mock.calls.map((c) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toContain('nodes');
    expect(keys).toContain('topology-graph');
    expect(keys).toContain('topology-groups');
    expect(keys).toContain('dashboard-summary');
    expect(keys).toContain('metrics-summary');
    expect(keys).toHaveLength(5);
  });

  it('NodeRejected uses same invalidation group as NodeApproved', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeRejected));

    const keys = invalidateSpy.mock.calls.map((c) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toHaveLength(5);
    expect(keys).toContain('nodes');
  });

  it('NodeDisabled uses same invalidation group as NodeApproved', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeDisabled));

    const keys = invalidateSpy.mock.calls.map((c) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toHaveLength(5);
    expect(keys).toContain('nodes');
  });

  it('NodeEnabled uses same invalidation group as NodeApproved', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.NodeEnabled));

    const keys = invalidateSpy.mock.calls.map((c) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toHaveLength(5);
    expect(keys).toContain('nodes');
  });

  it('SyncCycleCompleted invalidates dashboard-summary, events, incoming-batches, outgoing-batches, batch-errors, metrics-summary', async () => {
    await routeToCache(queryClient, makeEvent(OperationsEventType.SyncCycleCompleted));

    const keys = invalidateSpy.mock.calls.map((c) => (c[0] as { queryKey: string[] }).queryKey[0]);
    expect(keys).toContain('dashboard-summary');
    expect(keys).toContain('events');
    expect(keys).toContain('incoming-batches');
    expect(keys).toContain('outgoing-batches');
    expect(keys).toContain('batch-errors');
    expect(keys).toContain('metrics-summary');
    expect(keys).toHaveLength(6);
  });
});
```

- [ ] **Step 5: Create `notifications.test.ts`**

Create `src/MSOSync.Frontend/src/shared/signalr/notifications.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}));

import { toast } from 'sonner';
import { routeToToast } from './notifications';
import { OperationsEventType, type OperationsEvent } from './types';

function makeEvent(
  type: OperationsEventType,
  overrides: Partial<OperationsEvent> = {},
): OperationsEvent {
  return {
    type,
    nodeId: 'node-1',
    nodeLabel: 'NodeAlpha',
    previousStatus: null,
    currentStatus: null,
    occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 0)).toISOString(),
    groupId: null,
    ...overrides,
  };
}

describe('routeToToast', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Reset the dedup map between tests by varying timestamps
  });

  it('NodeHealthChanged Reachable → shows success toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Degraded',
      currentStatus: 'Reachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 1)).toISOString(),
    }));
    expect(toast.success).toHaveBeenCalledWith('Node NodeAlpha is reachable again.');
  });

  it('NodeHealthChanged * → Degraded shows warning toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Reachable',
      currentStatus: 'Degraded',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 2)).toISOString(),
    }));
    expect(toast.warning).toHaveBeenCalledWith('Node NodeAlpha is degraded.');
  });

  it('NodeHealthChanged * → Unreachable shows error toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Reachable',
      currentStatus: 'Unreachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 3)).toISOString(),
    }));
    expect(toast.error).toHaveBeenCalledWith('Node NodeAlpha is unreachable.');
  });

  it('NodeHealthChanged Unreachable → Unreachable shows no toast (already unreachable)', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Unreachable',
      currentStatus: 'Unreachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 4)).toISOString(),
    }));
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.warning).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('NodeApproved → shows success toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 5)).toISOString(),
    }));
    expect(toast.success).toHaveBeenCalledWith('Node NodeAlpha approved.');
  });

  it('NodeRejected → shows warning toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeRejected, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 6)).toISOString(),
    }));
    expect(toast.warning).toHaveBeenCalledWith('Node NodeAlpha registration rejected.');
  });

  it('NodeDisabled → shows warning toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeDisabled, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 7)).toISOString(),
    }));
    expect(toast.warning).toHaveBeenCalledWith('Node NodeAlpha disabled.');
  });

  it('NodeEnabled → shows info toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeEnabled, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 8)).toISOString(),
    }));
    expect(toast.info).toHaveBeenCalledWith('Node NodeAlpha re-enabled.');
  });

  it('SyncCycleCompleted → no toast (silent)', () => {
    routeToToast(makeEvent(OperationsEventType.SyncCycleCompleted, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 9)).toISOString(),
    }));
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.warning).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.info).not.toHaveBeenCalled();
  });

  it('dedup: same key in same bucket → second call suppressed', () => {
    const ts = new Date(Date.UTC(2026, 0, 1, 13, 0, 10)).toISOString();
    const evt = makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-dedup',
      nodeLabel: 'DedupNode',
      currentStatus: null,
      occurredAt: ts,
    });

    routeToToast(evt);
    routeToToast(evt);

    expect(toast.success).toHaveBeenCalledTimes(1);
  });

  it('dedup: same key in next bucket → shown again', () => {
    const bucket1 = new Date(Date.UTC(2026, 0, 1, 14, 0, 0)).toISOString();
    const bucket2 = new Date(Date.UTC(2026, 0, 1, 14, 0, 31)).toISOString(); // +31s = next bucket

    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-bucket',
      nodeLabel: 'BucketNode',
      currentStatus: null,
      occurredAt: bucket1,
    }));
    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-bucket',
      nodeLabel: 'BucketNode',
      currentStatus: null,
      occurredAt: bucket2,
    }));

    expect(toast.success).toHaveBeenCalledTimes(2);
  });

  it('dedup: same node different currentStatus → different key → both shown', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      nodeId: 'node-status',
      previousStatus: 'Reachable',
      currentStatus: 'Degraded',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 15, 0, 0)).toISOString(),
    }));
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      nodeId: 'node-status',
      previousStatus: 'Degraded',
      currentStatus: 'Unreachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 15, 0, 1)).toISOString(),
    }));

    expect(toast.warning).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledTimes(1);
  });

  it('nodeLabel falls back to nodeId when null', () => {
    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-fallback',
      nodeLabel: null,
      occurredAt: new Date(Date.UTC(2026, 0, 1, 16, 0, 0)).toISOString(),
    }));
    expect(toast.success).toHaveBeenCalledWith('Node node-fallback approved.');
  });
});
```

- [ ] **Step 6: Update `query.ts` constant**

Open `src/MSOSync.Frontend/src/shared/constants/query.ts`. Current:
```ts
export const DASHBOARD_REFRESH_MS = 30_000;
```

Replace with:
```ts
export const DASHBOARD_REFRESH_MS = 300_000;
```

- [ ] **Step 7: Update `dashboard/hooks.ts`**

Open `src/MSOSync.Frontend/src/features/dashboard/hooks.ts`. Current:
```ts
export function useDashboardSummary() {
  return useQuery({
    queryKey: queryKeys.dashboardSummary(),
    queryFn: getDashboardSummary,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  });
}
```

Add `staleTime: 60_000` (so window-focus refetch only fires when data is stale):
```ts
export function useDashboardSummary() {
  return useQuery({
    queryKey: queryKeys.dashboardSummary(),
    queryFn: getDashboardSummary,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 60_000,
  });
}
```

`useDashboardActivity` already has `refetchOnWindowFocus: false` and `staleTime: 30_000` — leave unchanged.

- [ ] **Step 8: Update `metrics/hooks.ts`**

Open `src/MSOSync.Frontend/src/features/metrics/hooks.ts`. The `DASHBOARD_REFRESH_MS` constant change automatically takes effect for `useMetricsSummary` and `useRuntimeMetrics`. Add `staleTime: 60_000` to those two live hooks:

```ts
export function useMetricsSummary() {
  return useQuery({
    queryKey: queryKeys.metricsSummary(),
    queryFn: getMetricsSummary,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 60_000,
  });
}

export function useRuntimeMetrics() {
  return useQuery({
    queryKey: queryKeys.runtimeMetrics(),
    queryFn: getRuntimeMetrics,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 60_000,
  });
}
```

Leave `useNodeMetrics` and `useChannelMetrics` unchanged (already have `staleTime: 30_000, refetchOnWindowFocus: false`).

- [ ] **Step 9: Update historical hooks — add `refetchOnWindowFocus: true`**

**`src/MSOSync.Frontend/src/features/events/hooks.ts`:**

Current:
```ts
export function useEvents(filter: EventFilter) {
  return useQuery({
    queryKey: queryKeys.events(filter),
    queryFn: () => getEvents(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
```

Updated:
```ts
export function useEvents(filter: EventFilter) {
  return useQuery({
    queryKey: queryKeys.events(filter),
    queryFn: () => getEvents(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}
```

**`src/MSOSync.Frontend/src/features/incoming-batches/hooks.ts`:**

Current:
```ts
export function useIncomingBatches(filter: IncomingBatchFilter) {
  return useQuery({
    queryKey: queryKeys.incomingBatches(filter),
    queryFn: () => getIncomingBatches(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
```

Updated:
```ts
export function useIncomingBatches(filter: IncomingBatchFilter) {
  return useQuery({
    queryKey: queryKeys.incomingBatches(filter),
    queryFn: () => getIncomingBatches(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: true,
    staleTime: 0,
  });
}
```

**`src/MSOSync.Frontend/src/features/outgoing-batches/hooks.ts`:** Apply same pattern (read the file first to see current content, then add `refetchOnWindowFocus: true` and `staleTime: 0`).

**`src/MSOSync.Frontend/src/features/batch-errors/hooks.ts`:** Apply same pattern.

- [ ] **Step 10: Update metadata hooks — add `staleTime: Infinity`**

For each of the following files, read the current content and add `staleTime: Infinity` to the query options. The `refetchOnWindowFocus: false` and `refetchInterval: false` (or absent) must be preserved as-is.

**`src/MSOSync.Frontend/src/features/users/hooks.ts`:** Add `staleTime: Infinity` to `useUsers`.

**`src/MSOSync.Frontend/src/features/channels/hooks.ts`:** Add `staleTime: Infinity` to `useChannels`.

**`src/MSOSync.Frontend/src/features/triggers/hooks.ts`:** Add `staleTime: Infinity` to all trigger hooks.

**`src/MSOSync.Frontend/src/features/routers/hooks.ts`:** Add `staleTime: Infinity` to all router hooks.

**`src/MSOSync.Frontend/src/features/parameters/hooks.ts`:** Add `staleTime: Infinity` to all parameter hooks.

For each file:
1. Read the current content
2. Add `staleTime: Infinity` to each `useQuery` call that does NOT already have it
3. Do NOT add `refetchInterval` or change existing options — only add `staleTime: Infinity`

- [ ] **Step 11: Run TypeScript type check**

```pwsh
cd src/MSOSync.Frontend
npx tsc -b --noEmit
```

Expected: no errors. Common issues:
- If `toast[severity]` errors: `severity` must be typed as `'success' | 'warning' | 'error' | 'info'`
- If `invalidateQueries` signature mismatch: verify `{ queryKey: string[] }` arg shape

- [ ] **Step 12: Run all Vitest tests**

```pwsh
cd src/MSOSync.Frontend
npm run test
```

Expected: all tests pass. Count should include:
- `types.test.ts`: 1 test
- `useSignalR.test.ts`: 1 test
- `eventRouter.test.ts`: 6 tests
- `notifications.test.ts`: 13 tests
- `dagre-layout.test.ts`: 6 tests (existing, no regression)

- [ ] **Step 13: Run production build**

```pwsh
cd src/MSOSync.Frontend
npm run build
```

Expected: exit 0, no errors.

- [ ] **Step 14: Commit**

```pwsh
cd D:\MSOSync
git add src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts
git add src/MSOSync.Frontend/src/shared/signalr/notifications.ts
git add src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx
git add src/MSOSync.Frontend/src/shared/signalr/eventRouter.test.ts
git add src/MSOSync.Frontend/src/shared/signalr/notifications.test.ts
git add src/MSOSync.Frontend/src/shared/constants/query.ts
git add src/MSOSync.Frontend/src/features/dashboard/hooks.ts
git add src/MSOSync.Frontend/src/features/metrics/hooks.ts
git add src/MSOSync.Frontend/src/features/events/hooks.ts
git add src/MSOSync.Frontend/src/features/incoming-batches/hooks.ts
git add src/MSOSync.Frontend/src/features/outgoing-batches/hooks.ts
git add src/MSOSync.Frontend/src/features/batch-errors/hooks.ts
git add src/MSOSync.Frontend/src/features/users/hooks.ts
git add src/MSOSync.Frontend/src/features/channels/hooks.ts
git add src/MSOSync.Frontend/src/features/triggers/hooks.ts
git add src/MSOSync.Frontend/src/features/routers/hooks.ts
git add src/MSOSync.Frontend/src/features/parameters/hooks.ts
git commit -m "feat(11C): add event router, toast notifications, polling policy updates"
```

## Report Contract

Return: `DONE`, last commit SHA, `tsc --noEmit` result (clean), test results (count + all pass), build result (exit 0), any concerns. Write full report to the report file path provided by the coordinator.
