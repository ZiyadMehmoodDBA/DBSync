# Epic 12B-1 Task 5: Frontend Foundation — Types, API, Badges, Hooks, SignalR

> Task 5 of 7. Master plan: `2026-07-06-epic12b1-node-lifecycle-engine.md`. Spec §8, §11.1. Global Constraints apply. Requires Task 4 (API contract live). Frontend root: `src/MSOSync.Frontend`.

**Goal:** Mirror the backend lifecycle contract in TypeScript, ship the three badges + `NodeStatusSummary` composite, the lifecycle API layer + TanStack Query hooks, and the SignalR category router extension — all Vitest-covered, `npm run build` green.

**Files (paths relative to `src/MSOSync.Frontend/src`):**
- Create: `shared/types/lifecycle.ts`
- Create: `shared/api/lifecycle.ts`
- Create: `shared/components/node/LifecycleBadge.tsx`
- Create: `shared/components/node/ConnectivityBadge.tsx`
- Create: `shared/components/node/MaintenanceBadge.tsx`
- Create: `shared/components/node/NodeStatusSummary.tsx`
- Create: `shared/components/node/index.ts`
- Create: `shared/components/node/__tests__/badges.test.tsx`
- Create: `shared/hooks/useNodeLifecycle.ts`
- Modify: `shared/types/nodes.ts` (NodeDto: `status`→`lifecycleState`, `syncEnabled`→`canSynchronize`, add `connectivityStatus`, `maintenanceMode`)
- Modify: `shared/types/index.ts` (re-export lifecycle types)
- Modify: `shared/types/permissions.ts` (+2 permission keys)
- Modify: `shared/queryKeys.ts` (+3 keys)
- Modify: `shared/signalr/types.ts` (+2 event types, +2 event fields)
- Modify: `shared/signalr/eventRouter.ts` (category routing)
- Modify: `shared/signalr/notifications.ts` (lifecycle toasts + CorrelationId dedupe)
- Modify: `features/nodes/columns.ts` + `features/nodes/NodesPage.tsx` (minimal compile fix only — full redesign is Task 6)
- Test: extend `shared/signalr/eventRouter.test.ts`, `shared/signalr/notifications.test.ts`

**Interfaces:**
- Consumes: Task 4 routes (`/node-lifecycle/nodes/{id}/state|transitions|history`, POST actions), `OperationsEvent` with `correlationId`/`trigger`, existing `client.ts` axios instance, `StatusBadge` styling conventions, `queryKeys`, sonner `toast`, `getErrorMessage`.
- Produces (Task 6 relies on):
  - All types in `shared/types/lifecycle.ts` (Step 2 — exact)
  - `lifecycleApi` functions (Step 3 — exact names)
  - Hooks: `useNodeState(nodeId)`, `useNodeTransitions(nodeId)`, `useNodeLifecycleHistory(nodeId, filter)`, `useEnableNode()`, `useDisableNode()`, `useStartMaintenance()`, `useEndMaintenance()`, `useDecommissionNode()`, `useForceCompleteDecommission()`
  - `<NodeStatusSummary lifecycle connectivity connectivityReason maintenance />`, individual badges
  - `queryKeys.nodeState(id)`, `queryKeys.nodeTransitions(id)`, `queryKeys.nodeLifecycleHistory(id)`
  - `PermissionKeys.ManageNodeLifecycle`, `PermissionKeys.ProvisionNodes`

---

## Steps

- [ ] **Step 1: Failing badge tests**

```tsx
// shared/components/node/__tests__/badges.test.tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LifecycleBadge } from '../LifecycleBadge';
import { ConnectivityBadge } from '../ConnectivityBadge';
import { MaintenanceBadge } from '../MaintenanceBadge';
import { NodeStatusSummary } from '../NodeStatusSummary';

describe('LifecycleBadge', () => {
  it('renders label text (never color-only)', () => {
    render(<LifecycleBadge state="Active" />);
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('renders an icon for every state', () => {
    const states = [
      'PendingApproval', 'PendingRegistration', 'Active', 'Recovery',
      'Disabled', 'Decommissioning', 'Decommissioned', 'Rejected',
    ] as const;
    for (const s of states) {
      const { container, unmount } = render(<LifecycleBadge state={s} />);
      expect(container.querySelector('svg')).not.toBeNull();
      unmount();
    }
  });
});

describe('ConnectivityBadge', () => {
  it('shows reason as title tooltip when provided', () => {
    render(<ConnectivityBadge status="Degraded" reason="HeartbeatStale" />);
    expect(screen.getByText('Degraded')).toBeInTheDocument();
    expect(screen.getByTitle('HeartbeatStale')).toBeInTheDocument();
  });
});

describe('MaintenanceBadge', () => {
  it('renders nothing when not in maintenance', () => {
    const { container } = render(<MaintenanceBadge active={false} />);
    expect(container.firstChild).toBeNull();
  });
  it('renders Maintenance label when active', () => {
    render(<MaintenanceBadge active reason="patching" />);
    expect(screen.getByText('Maintenance')).toBeInTheDocument();
  });
});

describe('NodeStatusSummary', () => {
  it('composes all three dimensions', () => {
    render(
      <NodeStatusSummary
        lifecycle="Active"
        connectivity="Reachable"
        connectivityReason="Healthy"
        maintenance
        maintenanceReason="patch window"
      />,
    );
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Reachable')).toBeInTheDocument();
    expect(screen.getByText('Maintenance')).toBeInTheDocument();
  });
});
```

Run: `npm run test -- badges` (from `src/MSOSync.Frontend`). Expected: FAIL (components missing).

- [ ] **Step 2: Lifecycle types (exact backend mirror)**

```typescript
// shared/types/lifecycle.ts
export type NodeLifecycleState =
  | 'PendingApproval'
  | 'PendingRegistration'
  | 'Active'
  | 'Recovery'
  | 'Disabled'
  | 'Decommissioning'
  | 'Decommissioned'
  | 'Rejected';

export type ConnectivityStatusName = 'Unknown' | 'Reachable' | 'Degraded' | 'Unreachable';

export type ConnectivityReason =
  | 'NotEvaluated'
  | 'NoHeartbeat'
  | 'Healthy'
  | 'HeartbeatStale'
  | 'HeartbeatExpired'
  | 'ProbeFailed'
  | 'ProbeFailures'
  | 'PendingActivation';

export type LifecycleTrigger =
  | 'Manual'
  | 'Registration'
  | 'Activation'
  | 'Recovery'
  | 'System'
  | 'Timeout'
  | 'Migration';

export interface NodeStateDto {
  nodeId: string;
  lifecycleState: NodeLifecycleState;
  connectivityStatus: ConnectivityStatusName;
  connectivityReason: string | null;
  lastHeartbeatUtc: string | null;
  lastProbeUtc: string | null;
  maintenanceMode: boolean;
  maintenanceReason: string | null;
  maintenanceUntil: string | null;
  decommissionInProgress: boolean;
  drainProgressPercent: number | null;
  decommissionGraceUntil: string | null;
}

export type LifecycleDangerLevel = 'Normal' | 'Critical';

export type LifecycleAction =
  | 'Enable'
  | 'Disable'
  | 'StartMaintenance'
  | 'EndMaintenance'
  | 'Decommission'
  | 'ForceCompleteDecommission';

export interface TransitionActionDto {
  action: LifecycleAction;
  requiresReason: boolean;
  requiresConfirmation: boolean;
  dangerLevel: LifecycleDangerLevel;
}

export interface TransitionsDto {
  currentState: NodeLifecycleState;
  allowedTransitions: TransitionActionDto[];
}

export interface LifecycleHistoryDto {
  historyId: number;
  nodeId: string;
  fromState: NodeLifecycleState | null;
  toState: NodeLifecycleState;
  trigger: LifecycleTrigger;
  reason: string | null;
  actor: string;
  correlationId: string | null;
  metadataJson: string | null;
  occurredAt: string;
}

export interface LifecycleHistoryPage {
  items: LifecycleHistoryDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface LifecycleHistoryFilter {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  trigger?: LifecycleTrigger;
}
```

Re-export from `shared/types/index.ts` following its existing pattern (`export * from './lifecycle';`).

Update `shared/types/nodes.ts` `NodeDto`: replace `status: string` with `lifecycleState: NodeLifecycleState`, replace `syncEnabled: boolean` with `canSynchronize: boolean`, and add `connectivityStatus: ConnectivityStatusName; maintenanceMode: boolean;` **if and only if** the backend `NodeDto` projection includes them after Task 1 edit 9 — open `src/MSOSync.Metadata/Dtos/NodeDto.cs` and mirror it field-for-field.

Update `shared/types/permissions.ts`:

```typescript
ProvisionNodes:      'PROVISION_NODES',
ManageNodeLifecycle: 'MANAGE_NODE_LIFECYCLE',
```

- [ ] **Step 3: API layer + query keys**

```typescript
// shared/api/lifecycle.ts
import { client } from './client';
import type {
  LifecycleHistoryFilter, LifecycleHistoryPage, NodeStateDto, TransitionsDto,
} from '../types/lifecycle';

const base = (nodeId: string) => `/node-lifecycle/nodes/${encodeURIComponent(nodeId)}`;

export async function getNodeState(nodeId: string, options?: { signal?: AbortSignal }): Promise<NodeStateDto> {
  const { data } = await client.get<NodeStateDto>(`${base(nodeId)}/state`, options);
  return data;
}

export async function getNodeTransitions(nodeId: string, options?: { signal?: AbortSignal }): Promise<TransitionsDto> {
  const { data } = await client.get<TransitionsDto>(`${base(nodeId)}/transitions`, options);
  return data;
}

export async function getNodeLifecycleHistory(
  nodeId: string, filter: LifecycleHistoryFilter = {}, options?: { signal?: AbortSignal },
): Promise<LifecycleHistoryPage> {
  const { data } = await client.get<LifecycleHistoryPage>(`${base(nodeId)}/history`, {
    params: filter, ...options,
  });
  return data;
}

export async function enableNode(nodeId: string): Promise<void> {
  await client.post(`${base(nodeId)}/enable`);
}

export async function disableNode(nodeId: string, reason?: string): Promise<void> {
  await client.post(`${base(nodeId)}/disable`, { reason: reason ?? null });
}

export async function startMaintenance(
  nodeId: string, body: { reason: string; expectedEndAt?: string; notifyNode: boolean },
): Promise<void> {
  await client.post(`${base(nodeId)}/maintenance/start`, body);
}

export async function endMaintenance(nodeId: string): Promise<void> {
  await client.post(`${base(nodeId)}/maintenance/end`);
}

export async function decommissionNode(
  nodeId: string, body: { reason: string; gracePeriodMinutes?: number },
): Promise<void> {
  await client.post(`${base(nodeId)}/decommission`, body);
}

export async function forceCompleteDecommission(nodeId: string): Promise<void> {
  await client.post(`${base(nodeId)}/decommission/force`);
}
```

(If `client.ts` exports the axios instance under a different name — e.g. default export or `api` — match it; check `shared/api/nodes.ts` imports.)

`shared/queryKeys.ts` — add:

```typescript
nodeState:            (id: string) => ['node-state', id],
nodeTransitions:      (id: string) => ['node-transitions', id],
nodeLifecycleHistory: (id: string, filter?: unknown) =>
  filter ? ['node-lifecycle-history', id, filter] : ['node-lifecycle-history', id],
```

- [ ] **Step 4: Badges + NodeStatusSummary**

```tsx
// shared/components/node/LifecycleBadge.tsx
import {
  CheckCircle2, CircleOff, Clock, HardDriveDownload, LifeBuoy, Trash2, XCircle, KeyRound,
} from 'lucide-react';
import type { NodeLifecycleState } from '../../types/lifecycle';
import { cn } from '../../../lib/utils';

// Color + icon + label — state is never encoded by color alone (spec §11.1).
const META: Record<NodeLifecycleState, { icon: typeof CheckCircle2; className: string }> = {
  PendingApproval:     { icon: Clock,             className: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400' },
  PendingRegistration: { icon: KeyRound,          className: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-400' },
  Active:              { icon: CheckCircle2,      className: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400' },
  Recovery:            { icon: LifeBuoy,          className: 'bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-400' },
  Disabled:            { icon: CircleOff,         className: 'bg-neutral-200 text-neutral-700 dark:bg-neutral-800 dark:text-neutral-300' },
  Decommissioning:     { icon: HardDriveDownload, className: 'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-400' },
  Decommissioned:      { icon: Trash2,            className: 'bg-neutral-100 text-neutral-500 dark:bg-neutral-900 dark:text-neutral-500' },
  Rejected:            { icon: XCircle,           className: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400' },
};

export function LifecycleBadge({ state }: { state: NodeLifecycleState }) {
  const meta = META[state] ?? META.Disabled;
  const Icon = meta.icon;
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        meta.className,
      )}
    >
      <Icon className="h-3 w-3" aria-hidden />
      {state}
    </span>
  );
}
```

```tsx
// shared/components/node/ConnectivityBadge.tsx
import { Activity, AlertTriangle, HelpCircle, WifiOff } from 'lucide-react';
import type { ConnectivityStatusName } from '../../types/lifecycle';
import { cn } from '../../../lib/utils';

const META: Record<ConnectivityStatusName, { icon: typeof Activity; className: string }> = {
  Unknown:     { icon: HelpCircle,    className: 'bg-neutral-100 text-neutral-700 dark:bg-neutral-800 dark:text-neutral-300' },
  Reachable:   { icon: Activity,      className: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400' },
  Degraded:    { icon: AlertTriangle, className: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400' },
  Unreachable: { icon: WifiOff,       className: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400' },
};

export function ConnectivityBadge({
  status, reason,
}: { status: ConnectivityStatusName; reason?: string | null }) {
  const meta = META[status] ?? META.Unknown;
  const Icon = meta.icon;
  return (
    <span
      title={reason ?? undefined}
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        meta.className,
      )}
    >
      <Icon className="h-3 w-3" aria-hidden />
      {status}
    </span>
  );
}
```

```tsx
// shared/components/node/MaintenanceBadge.tsx
import { Wrench } from 'lucide-react';

export function MaintenanceBadge({
  active, reason,
}: { active: boolean; reason?: string | null }) {
  if (!active) return null;
  return (
    <span
      title={reason ?? undefined}
      className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-900/30 dark:text-amber-400"
    >
      <Wrench className="h-3 w-3" aria-hidden />
      Maintenance
    </span>
  );
}
```

```tsx
// shared/components/node/NodeStatusSummary.tsx
import type { ConnectivityStatusName, NodeLifecycleState } from '../../types/lifecycle';
import { LifecycleBadge } from './LifecycleBadge';
import { ConnectivityBadge } from './ConnectivityBadge';
import { MaintenanceBadge } from './MaintenanceBadge';

export interface NodeStatusSummaryProps {
  lifecycle: NodeLifecycleState;
  connectivity: ConnectivityStatusName;
  connectivityReason?: string | null;
  maintenance?: boolean;
  maintenanceReason?: string | null;
}

/** The single composite renderer for node state — used everywhere (spec §11.1). */
export function NodeStatusSummary(p: NodeStatusSummaryProps) {
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      <LifecycleBadge state={p.lifecycle} />
      <ConnectivityBadge status={p.connectivity} reason={p.connectivityReason} />
      <MaintenanceBadge active={p.maintenance ?? false} reason={p.maintenanceReason} />
    </span>
  );
}
```

```typescript
// shared/components/node/index.ts
export { LifecycleBadge } from './LifecycleBadge';
export { ConnectivityBadge } from './ConnectivityBadge';
export { MaintenanceBadge } from './MaintenanceBadge';
export { NodeStatusSummary } from './NodeStatusSummary';
export type { NodeStatusSummaryProps } from './NodeStatusSummary';
```

Run badge tests: `npm run test -- badges`. Expected: PASS.

- [ ] **Step 5: Query + mutation hooks**

```typescript
// shared/hooks/useNodeLifecycle.ts
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  decommissionNode, disableNode, enableNode, endMaintenance, forceCompleteDecommission,
  getNodeLifecycleHistory, getNodeState, getNodeTransitions, startMaintenance,
} from '../api/lifecycle';
import type { LifecycleHistoryFilter } from '../types/lifecycle';
import { getErrorMessage } from '../utils/error';
import { queryKeys } from '../queryKeys';

export function useNodeState(nodeId: string) {
  return useQuery({
    queryKey: queryKeys.nodeState(nodeId),
    queryFn: ({ signal }) => getNodeState(nodeId, { signal }),
  });
}

export function useNodeTransitions(nodeId: string) {
  return useQuery({
    queryKey: queryKeys.nodeTransitions(nodeId),
    queryFn: ({ signal }) => getNodeTransitions(nodeId, { signal }),
  });
}

export function useNodeLifecycleHistory(nodeId: string, filter: LifecycleHistoryFilter = {}) {
  return useQuery({
    queryKey: queryKeys.nodeLifecycleHistory(nodeId, filter),
    queryFn: ({ signal }) => getNodeLifecycleHistory(nodeId, filter, { signal }),
  });
}

export function invalidateLifecycle(qc: QueryClient, nodeId: string) {
  void qc.invalidateQueries({ queryKey: ['nodes'] });
  void qc.invalidateQueries({ queryKey: queryKeys.nodeState(nodeId) });
  void qc.invalidateQueries({ queryKey: queryKeys.nodeTransitions(nodeId) });
  void qc.invalidateQueries({ queryKey: ['node-lifecycle-history', nodeId] });
  void qc.invalidateQueries({ queryKey: ['node-management', 'overview'] });
  void qc.invalidateQueries({ queryKey: queryKeys.topologyGraph() });
  void qc.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
  void qc.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
}

function lifecycleMutation<TArgs>(
  fn: (args: TArgs) => Promise<void>,
  successMessage: string,
  nodeIdOf: (args: TArgs) => string,
) {
  // Factory keeps the 10C/10D toast-on-settled pattern in ONE place.
  return function useLifecycleMutation() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: fn,
      onSuccess: (_data, args) => {
        toast.success(successMessage);
        invalidateLifecycle(qc, nodeIdOf(args));
      },
      onError: (error) => toast.error(getErrorMessage(error)),
    });
  };
}

export const useEnableNode = lifecycleMutation(
  (a: { nodeId: string }) => enableNode(a.nodeId), 'Node enabled', (a) => a.nodeId);

export const useDisableNode = lifecycleMutation(
  (a: { nodeId: string; reason?: string }) => disableNode(a.nodeId, a.reason),
  'Node disabled', (a) => a.nodeId);

export const useStartMaintenance = lifecycleMutation(
  (a: { nodeId: string; reason: string; expectedEndAt?: string; notifyNode: boolean }) =>
    startMaintenance(a.nodeId, { reason: a.reason, expectedEndAt: a.expectedEndAt, notifyNode: a.notifyNode }),
  'Maintenance started', (a) => a.nodeId);

export const useEndMaintenance = lifecycleMutation(
  (a: { nodeId: string }) => endMaintenance(a.nodeId), 'Maintenance ended', (a) => a.nodeId);

export const useDecommissionNode = lifecycleMutation(
  (a: { nodeId: string; reason: string; gracePeriodMinutes?: number }) =>
    decommissionNode(a.nodeId, { reason: a.reason, gracePeriodMinutes: a.gracePeriodMinutes }),
  'Decommission started', (a) => a.nodeId);

export const useForceCompleteDecommission = lifecycleMutation(
  (a: { nodeId: string }) => forceCompleteDecommission(a.nodeId),
  'Decommission completed', (a) => a.nodeId);
```

- [ ] **Step 6: SignalR extension (category router + toasts)**

`shared/signalr/types.ts` — add to `OperationsEventType`:

```typescript
NodeLifecycleChanged:   'NodeLifecycleChanged',
NodeMaintenanceChanged: 'NodeMaintenanceChanged',
```

Extend `OperationsEvent` interface:

```typescript
correlationId?: string | null;
trigger?: string | null;
```

`shared/signalr/eventRouter.ts` — add cases to `routeToCache` (Category → invalidation table, spec §8):

```typescript
case OperationsEventType.NodeLifecycleChanged:
  // Lifecycle category: nodes grid, node-management overview, topology, history, node state
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['nodes'] }),
    queryClient.invalidateQueries({ queryKey: ['node-management'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-groups'] }),
    queryClient.invalidateQueries({ queryKey: ['node-state', event.nodeId] }),
    queryClient.invalidateQueries({ queryKey: ['node-transitions', event.nodeId] }),
    queryClient.invalidateQueries({ queryKey: ['node-lifecycle-history', event.nodeId] }),
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
  ]);
  return;
case OperationsEventType.NodeMaintenanceChanged:
  // Maintenance category: same minus history
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['nodes'] }),
    queryClient.invalidateQueries({ queryKey: ['node-management'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
    queryClient.invalidateQueries({ queryKey: ['node-state', event.nodeId] }),
    queryClient.invalidateQueries({ queryKey: ['node-transitions', event.nodeId] }),
  ]);
  return;
```

(Invalidation is naturally idempotent — duplicate delivery is harmless, spec §8. Match the file's existing return/await style.)

`shared/signalr/notifications.ts` — add lifecycle toast routing with CorrelationId dedupe:

```typescript
// Toast dedupe by CorrelationId (spec §8: events idempotent — duplicate delivery, no duplicate toasts)
const seenCorrelationIds = new Set<string>();
function dedupe(correlationId: string | null | undefined): boolean {
  if (!correlationId) return false;
  if (seenCorrelationIds.has(correlationId)) return true;
  seenCorrelationIds.add(correlationId);
  if (seenCorrelationIds.size > 500) {
    const first = seenCorrelationIds.values().next().value;
    if (first) seenCorrelationIds.delete(first);
  }
  return false;
}

// Toast catalogue (spec §8): Activated, Enabled, Disabled, Maintenance Started/Ended,
// Decommission Started/Completed. Connectivity = silent badge updates, never a toast.
// (Recovery Approved toast comes from the approve mutation hook, not SignalR — no state change event.)
export function lifecycleToastMessage(event: OperationsEvent): string | null {
  if (event.type === OperationsEventType.NodeMaintenanceChanged) {
    return event.currentStatus === 'MaintenanceOn'
      ? `Node ${event.nodeId}: maintenance started`
      : `Node ${event.nodeId}: maintenance ended`;
  }
  if (event.type !== OperationsEventType.NodeLifecycleChanged) return null;
  switch (event.currentStatus) {
    case 'Active':
      return event.trigger === 'Activation'
        ? `Node ${event.nodeId} activated`
        : `Node ${event.nodeId} enabled`;
    case 'Disabled':         return `Node ${event.nodeId} disabled`;
    case 'Decommissioning':  return `Node ${event.nodeId}: decommission started`;
    case 'Decommissioned':   return `Node ${event.nodeId}: decommission completed`;
    default:                 return null;   // Recovery entry etc. — queue badge, not a toast
  }
}
```

Wire into the existing `routeToToast` (or equivalent) function: if `dedupe(event.correlationId)` → return; else `const msg = lifecycleToastMessage(event); if (msg) toast.info(msg);` — following the file's existing toast invocation style. Export `lifecycleToastMessage` and `dedupe` (as `_resetDedupeForTests` helper if needed) for the tests.

Extend `shared/signalr/eventRouter.test.ts` and `notifications.test.ts`:

```text
routeToCache_NodeLifecycleChanged_InvalidatesLifecycleCategoryKeys
routeToCache_NodeMaintenanceChanged_InvalidatesMaintenanceCategory_NotHistory
lifecycleToastMessage_ActivationTrigger_SaysActivated
lifecycleToastMessage_ManualToActive_SaysEnabled
lifecycleToastMessage_Decommissioning_SaysStarted
lifecycleToastMessage_RecoveryEntry_ReturnsNull
duplicateCorrelationId_SecondToastSuppressed
```

(Follow the mocking style already used in `eventRouter.test.ts` — queryClient mock with `invalidateQueries` spy.)

- [ ] **Step 7: Minimal compile fix for NodeDto rename**

`features/nodes/columns.ts`: `field: 'status'` → `field: 'lifecycleState'` (keep `StatusBadge` rendering for now — Task 6 replaces the column set entirely); delete the `syncEnabled` column object. `features/nodes/NodesPage.tsx` + `NodeDialog.tsx`/`schemas.ts`: fix any `status`/`syncEnabled` references the TypeScript compiler flags (rename only, no behavior change). The enable/disable/approve legacy mutations now point at deleted endpoints — leave the code compiling but expect Task 6 to replace the action wiring; if `shared/api/nodes.ts` functions reference removed routes, they still compile (runtime 404s acceptable mid-branch).

- [ ] **Step 8: Test + build**

```pwsh
cd src/MSOSync.Frontend
npm run test
npm run build
```

Expected: all Vitest suites green; `tsc -b && vite build` zero errors/warnings.

- [ ] **Step 9: Commit**

```pwsh
git add src/MSOSync.Frontend/src/shared/types/lifecycle.ts src/MSOSync.Frontend/src/shared/types/nodes.ts src/MSOSync.Frontend/src/shared/types/index.ts src/MSOSync.Frontend/src/shared/types/permissions.ts
git add src/MSOSync.Frontend/src/shared/api/lifecycle.ts src/MSOSync.Frontend/src/shared/queryKeys.ts src/MSOSync.Frontend/src/shared/hooks/useNodeLifecycle.ts
git add src/MSOSync.Frontend/src/shared/components/node
git add src/MSOSync.Frontend/src/shared/signalr
git add src/MSOSync.Frontend/src/features/nodes
git commit -m "feat(12B-1): frontend lifecycle foundation — types, API, badges, hooks, SignalR category routing"
```
