# Epic 12B-1 Task 6: Lifecycle UI — Grid, Dialogs, Wizard, Timeline, Recovery, Topology

> Task 6 of 7. Master plan: `2026-07-06-epic12b1-node-lifecycle-engine.md`. Spec §11. Global Constraints apply. Requires Task 5. Frontend root: `src/MSOSync.Frontend`, paths below relative to `src/MSOSync.Frontend/src`.

**Goal:** Operators drive the complete lifecycle from the UI: three-badge nodes grid with a transitions-driven action menu, maintenance dialog, 3-step decommission wizard, node lifecycle panel with history timeline, recovery review context, topology recolor — zero hardcoded transition rules, no legacy status model anywhere.

**Files:**
- Create: `features/node-management/nodes/components/NodesGrid.tsx` (replaces the `NodesTab` stub content)
- Create: `features/node-management/nodes/components/NodeActionsMenu.tsx`
- Create: `features/node-management/nodes/components/MaintenanceDialog.tsx`
- Create: `features/node-management/nodes/components/DecommissionWizard.tsx`
- Create: `features/node-management/nodes/components/NodeLifecyclePanel.tsx`
- Create: `features/node-management/nodes/components/LifecycleHistoryTimeline.tsx`
- Create: `features/node-management/nodes/components/__tests__/NodeActionsMenu.test.tsx`
- Create: `features/node-management/nodes/components/__tests__/LifecycleHistoryTimeline.test.tsx`
- Modify: `features/node-management/nodes/components/NodesTab.tsx` (stub → grid + detail panel layout)
- Modify: `features/node-management/registrations/components/RegistrationDetailPanel.tsx` (recovery context panel)
- Modify: `features/node-management/registrations/components/RegistrationQueue.tsx` (recovery badge)
- Modify: `features/topology/graph/TopologyGroupNode.tsx` + `features/topology/graph/constants.ts` (lifecycle + connectivity ring)
- Modify: `features/nodes/columns.ts`, `features/nodes/NodesPage.tsx`, `features/nodes/mutations.ts`, `shared/api/nodes.ts` (legacy action removal — Step 7)

**Interfaces:**
- Consumes (Task 5): `NodeStatusSummary` + badges, `useNodeState`, `useNodeTransitions`, `useNodeLifecycleHistory`, all six mutation hooks, `TransitionActionDto`/`LifecycleAction` types, `PermissionKeys.ManageNodeLifecycle`. Existing: `ActionMenu`/`ConfirmDialog` (`shared/components/actions`), `useHasPermission`, `DiffViewer`, `formatRelativeTime`, AG Grid setup from `features/nodes/NodesGrid.tsx`, wizard step pattern from `features/node-management/provision`.
- Produces: complete operator lifecycle UI (Definition of Done, spec §15).

---

## Steps

- [ ] **Step 1: Failing component tests**

```tsx
// features/node-management/nodes/components/__tests__/NodeActionsMenu.test.tsx
// Pattern: render with QueryClientProvider wrapper; mock shared/api/lifecycle with vi.mock.
import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { NodeActionsMenu } from '../NodeActionsMenu';

vi.mock('../../../../../shared/api/lifecycle', () => ({
  getNodeTransitions: vi.fn().mockResolvedValue({
    currentState: 'Active',
    allowedTransitions: [
      { action: 'Disable',          requiresReason: false, requiresConfirmation: true,  dangerLevel: 'Normal' },
      { action: 'StartMaintenance', requiresReason: true,  requiresConfirmation: false, dangerLevel: 'Normal' },
      { action: 'Decommission',     requiresReason: true,  requiresConfirmation: true,  dangerLevel: 'Critical' },
    ],
  }),
  enableNode: vi.fn(), disableNode: vi.fn(), startMaintenance: vi.fn(), endMaintenance: vi.fn(),
  decommissionNode: vi.fn(), forceCompleteDecommission: vi.fn(),
  getNodeState: vi.fn(), getNodeLifecycleHistory: vi.fn(),
}));

function wrap(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe('NodeActionsMenu', () => {
  it('renders exactly the actions the backend returns — no hardcoded rules', async () => {
    wrap(<NodeActionsMenu nodeId="n1" canManage onAction={() => {}} />);
    await userEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(screen.getByText('Disable')).toBeInTheDocument());
    expect(screen.getByText('Start Maintenance')).toBeInTheDocument();
    expect(screen.getByText('Decommission')).toBeInTheDocument();
    expect(screen.queryByText('Enable')).not.toBeInTheDocument();
  });

  it('hides mutating actions without MANAGE_NODE_LIFECYCLE', async () => {
    wrap(<NodeActionsMenu nodeId="n1" canManage={false} onAction={() => {}} />);
    await userEvent.click(screen.getByRole('button'));
    await waitFor(() =>
      expect(screen.getByText(/no permitted actions|view only/i)).toBeInTheDocument());
  });
});
```

```tsx
// features/node-management/nodes/components/__tests__/LifecycleHistoryTimeline.test.tsx
import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { TimelineEntry } from '../LifecycleHistoryTimeline';

describe('TimelineEntry', () => {
  const entry = {
    historyId: 1, nodeId: 'n1', fromState: 'PendingRegistration' as const,
    toState: 'Active' as const, trigger: 'Activation' as const, reason: null,
    actor: 'system', correlationId: 'abc-123', metadataJson: null,
    occurredAt: '2026-07-06T10:00:00Z',
  };

  it('shows from → to, trigger, actor', () => {
    render(<TimelineEntry entry={entry} />);
    expect(screen.getByText(/PendingRegistration/)).toBeInTheDocument();
    expect(screen.getByText(/Active/)).toBeInTheDocument();
    expect(screen.getByText(/Activation/)).toBeInTheDocument();
    expect(screen.getByText(/system/)).toBeInTheDocument();
  });

  it('renders migration seed rows (fromState null) as "entered lifecycle model"', () => {
    render(<TimelineEntry entry={{ ...entry, fromState: null, trigger: 'Migration' }} />);
    expect(screen.getByText(/entered lifecycle model/i)).toBeInTheDocument();
  });

  it('hides CorrelationId behind a collapsible detail', () => {
    render(<TimelineEntry entry={entry} />);
    expect(screen.queryByText('abc-123')).not.toBeVisible();
  });
});
```

Run `npm run test` — expected: FAIL (components missing).

- [ ] **Step 2: NodeActionsMenu (transitions-driven — the zero-hardcoding contract)**

```tsx
// features/node-management/nodes/components/NodeActionsMenu.tsx
import { useState } from 'react';
import { MoreVertical } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { getNodeTransitions } from '../../../../shared/api/lifecycle';
import { queryKeys } from '../../../../shared/queryKeys';
import type { TransitionActionDto } from '../../../../shared/types/lifecycle';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '../../../../components/ui/dropdown-menu';
import { Button } from '../../../../components/ui/button';

const LABELS: Record<string, string> = {
  Enable: 'Enable',
  Disable: 'Disable',
  StartMaintenance: 'Start Maintenance',
  EndMaintenance: 'End Maintenance',
  Decommission: 'Decommission',
  ForceCompleteDecommission: 'Force Complete Decommission',
};

export interface NodeActionsMenuProps {
  nodeId: string;
  canManage: boolean;
  onAction: (nodeId: string, action: TransitionActionDto) => void;
}

/**
 * Renders EXACTLY what GET /transitions returns. requiresReason / requiresConfirmation /
 * dangerLevel drive the downstream dialog choice in NodesGrid — this component encodes
 * zero transition rules (spec §11.2).
 */
export function NodeActionsMenu({ nodeId, canManage, onAction }: NodeActionsMenuProps) {
  const [open, setOpen] = useState(false);
  const { data, isLoading } = useQuery({
    queryKey: queryKeys.nodeTransitions(nodeId),
    queryFn: ({ signal }) => getNodeTransitions(nodeId, { signal }),
    enabled: open,                 // lazy: fetched when the menu opens
    staleTime: 5_000,
  });

  const actions = canManage ? (data?.allowedTransitions ?? []) : [];

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" className="h-7 w-7 p-0" aria-label="Node actions">
          <MoreVertical className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {isLoading && <DropdownMenuItem disabled>Loading…</DropdownMenuItem>}
        {!isLoading && actions.length === 0 && (
          <DropdownMenuItem disabled>
            {canManage ? 'No permitted actions' : 'View only'}
          </DropdownMenuItem>
        )}
        {actions.map((a) => (
          <DropdownMenuItem
            key={a.action}
            className={a.dangerLevel === 'Critical' ? 'text-red-600 focus:text-red-600' : undefined}
            onClick={() => onAction(nodeId, a)}
          >
            {LABELS[a.action] ?? a.action}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

(Match import paths/components to how `ActionMenu.tsx` imports the shadcn dropdown — reuse its exact primitives. If `ActionMenu` is preferred, extend it with an `onOpenChange` + async-items capability instead; the contract above is what matters.)

- [ ] **Step 3: MaintenanceDialog + DecommissionWizard**

```tsx
// features/node-management/nodes/components/MaintenanceDialog.tsx
import { useState } from 'react';
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle,
} from '../../../../components/ui/dialog';
import { Button } from '../../../../components/ui/button';
import { useStartMaintenance } from '../../../../shared/hooks/useNodeLifecycle';

export function MaintenanceDialog({
  nodeId, open, onOpenChange,
}: { nodeId: string; open: boolean; onOpenChange: (open: boolean) => void }) {
  const [reason, setReason] = useState('');
  const [expectedEndAt, setExpectedEndAt] = useState('');
  const [notifyNode, setNotifyNode] = useState(false);
  const mutation = useStartMaintenance();

  const submit = () => {
    mutation.mutate(
      {
        nodeId,
        reason: reason.trim(),
        expectedEndAt: expectedEndAt ? new Date(expectedEndAt).toISOString() : undefined,
        notifyNode,
      },
      { onSuccess: () => onOpenChange(false) },
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>Start Maintenance — {nodeId}</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <label className="block text-sm">
            Reason <span className="text-red-500">*</span>
            <textarea
              className="mt-1 w-full rounded border p-2 text-sm"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={2}
            />
          </label>
          <label className="block text-sm">
            Expected end (optional)
            <input
              type="datetime-local"
              className="mt-1 w-full rounded border p-2 text-sm"
              value={expectedEndAt}
              onChange={(e) => setExpectedEndAt(e.target.value)}
            />
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={notifyNode} onChange={(e) => setNotifyNode(e.target.checked)} />
            Notify node (best effort)
          </label>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button onClick={submit} disabled={!reason.trim() || mutation.isPending}>
            {mutation.isPending ? 'Starting…' : 'Start Maintenance'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

(Use the project's existing form primitives — if `EntityDialog`/`FormSection` from `shared/components/forms` are the house style for dialogs with inputs, prefer them; the field set and requiredness are the contract.)

```tsx
// features/node-management/nodes/components/DecommissionWizard.tsx
import { useState } from 'react';
import {
  Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle,
} from '../../../../components/ui/dialog';
import { Button } from '../../../../components/ui/button';
import { useDecommissionNode, useNodeState } from '../../../../shared/hooks/useNodeLifecycle';

const REASON_PRESETS = [
  'Hardware Replacement', 'Site Closure', 'Migration',
  'Duplicate Node', 'Security Incident', 'Manual',
] as const;

/** 3-step wizard (12A provision-wizard pattern, spec §11.3):
 *  1. reason preset + free text + grace period
 *  2. impact preview (drain snapshot + credential revocation warning)
 *  3. typed confirmation ("decommission")
 */
export function DecommissionWizard({
  nodeId, nodeName, open, onOpenChange,
}: { nodeId: string; nodeName: string; open: boolean; onOpenChange: (open: boolean) => void }) {
  const [step, setStep] = useState(1);
  const [preset, setPreset] = useState<string>('');
  const [reasonText, setReasonText] = useState('');
  const [graceMinutes, setGraceMinutes] = useState<number | ''>('');
  const [confirmText, setConfirmText] = useState('');
  const mutation = useDecommissionNode();
  const { data: state } = useNodeState(nodeId);   // heartbeat/connectivity context for impact step

  const reason = [preset, reasonText.trim()].filter(Boolean).join(': ');

  const submit = () => {
    mutation.mutate(
      { nodeId, reason, gracePeriodMinutes: graceMinutes === '' ? undefined : graceMinutes },
      { onSuccess: () => { onOpenChange(false); setStep(1); setConfirmText(''); } },
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Decommission {nodeName} — step {step} of 3</DialogTitle>
        </DialogHeader>

        {step === 1 && (
          <div className="space-y-3">
            <div className="text-sm font-medium">Reason</div>
            <div className="flex flex-wrap gap-2">
              {REASON_PRESETS.map((p) => (
                <Button
                  key={p}
                  size="sm"
                  variant={preset === p ? 'default' : 'outline'}
                  onClick={() => setPreset(p)}
                >
                  {p}
                </Button>
              ))}
            </div>
            <textarea
              className="w-full rounded border p-2 text-sm"
              placeholder="Details (required)"
              value={reasonText}
              onChange={(e) => setReasonText(e.target.value)}
              rows={2}
            />
            <label className="block text-sm">
              Grace period minutes (default 60)
              <input
                type="number"
                min={1}
                className="mt-1 w-full rounded border p-2 text-sm"
                value={graceMinutes}
                onChange={(e) => setGraceMinutes(e.target.value === '' ? '' : Number(e.target.value))}
              />
            </label>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3 text-sm">
            <p className="font-medium">Impact preview</p>
            <ul className="list-disc space-y-1 pl-5">
              <li>New sync work freezes immediately; in-flight batches drain until complete or grace expiry.</li>
              <li>
                Last heartbeat: {state?.lastHeartbeatUtc ?? 'never'} · Connectivity:{' '}
                {state?.connectivityStatus ?? 'Unknown'}
              </li>
              <li className="font-medium text-red-600">
                All node credentials (bootstrap + auth tokens) are revoked at start. This node can
                never rejoin under this identity — a returning machine must register as a new node.
              </li>
              <li>The node record is preserved permanently and hidden from default views.</li>
            </ul>
          </div>
        )}

        {step === 3 && (
          <div className="space-y-3 text-sm">
            <p>
              Type <span className="font-mono font-bold">decommission</span> to confirm.
            </p>
            <input
              className="w-full rounded border p-2 text-sm"
              value={confirmText}
              onChange={(e) => setConfirmText(e.target.value)}
              autoFocus
            />
          </div>
        )}

        <DialogFooter>
          {step > 1 && <Button variant="outline" onClick={() => setStep(step - 1)}>Back</Button>}
          {step < 3 && (
            <Button onClick={() => setStep(step + 1)} disabled={step === 1 && !reasonText.trim()}>
              Next
            </Button>
          )}
          {step === 3 && (
            <Button
              variant="destructive"
              disabled={confirmText !== 'decommission' || mutation.isPending}
              onClick={submit}
            >
              {mutation.isPending ? 'Starting…' : 'Decommission Node'}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

(Impact preview open-batch count: if `shared/api` exposes an outgoing-batches query filterable by node, add the live count as an extra `<li>`; otherwise the drain explanation + credential warning above satisfies the spec's intent — do not build a new backend endpoint for it.)

- [ ] **Step 4: NodesGrid + NodesTab + NodeLifecyclePanel + Timeline**

```tsx
// features/node-management/nodes/components/LifecycleHistoryTimeline.tsx
import { useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { useNodeLifecycleHistory } from '../../../../shared/hooks/useNodeLifecycle';
import type { LifecycleHistoryDto, LifecycleTrigger } from '../../../../shared/types/lifecycle';
import { LifecycleBadge } from '../../../../shared/components/node';
import { Button } from '../../../../components/ui/button';

const TRIGGERS: LifecycleTrigger[] = [
  'Manual', 'Registration', 'Activation', 'Recovery', 'System', 'Timeout', 'Migration',
];

export function TimelineEntry({ entry }: { entry: LifecycleHistoryDto }) {
  const [expanded, setExpanded] = useState(false);
  return (
    <li className="border-l-2 border-neutral-200 py-2 pl-4 dark:border-neutral-700">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        {entry.fromState === null ? (
          <span className="italic text-neutral-500">entered lifecycle model</span>
        ) : (
          <>
            <LifecycleBadge state={entry.fromState} />
            <span aria-hidden>→</span>
          </>
        )}
        <LifecycleBadge state={entry.toState} />
        <span className="text-neutral-500">{entry.trigger}</span>
        <span className="text-neutral-500">by {entry.actor}</span>
        <span className="ml-auto text-xs text-neutral-400">
          {new Date(entry.occurredAt).toLocaleString()}
        </span>
      </div>
      {entry.reason && <p className="mt-1 text-xs text-neutral-500">{entry.reason}</p>}
      {entry.correlationId && (
        <div className="mt-1">
          <button
            type="button"
            className="inline-flex items-center gap-1 text-xs text-neutral-400 hover:text-neutral-600"
            onClick={() => setExpanded(!expanded)}
          >
            {expanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
            details
          </button>
          <span className={expanded ? 'ml-2 font-mono text-xs' : 'hidden ml-2 font-mono text-xs'} hidden={!expanded}>
            {entry.correlationId}
          </span>
        </div>
      )}
    </li>
  );
}

export function LifecycleHistoryTimeline({ nodeId }: { nodeId: string }) {
  const [page, setPage] = useState(1);
  const [trigger, setTrigger] = useState<LifecycleTrigger | ''>('');
  const { data, isLoading } = useNodeLifecycleHistory(nodeId, {
    page, pageSize: 25, trigger: trigger || undefined,
  });

  // Group by day
  const groups = new Map<string, LifecycleHistoryDto[]>();
  for (const item of data?.items ?? []) {
    const day = new Date(item.occurredAt).toLocaleDateString();
    groups.set(day, [...(groups.get(day) ?? []), item]);
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <select
          className="rounded border p-1 text-sm"
          value={trigger}
          onChange={(e) => { setTrigger(e.target.value as LifecycleTrigger | ''); setPage(1); }}
        >
          <option value="">All triggers</option>
          {TRIGGERS.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
      </div>
      {isLoading && <p className="text-sm text-neutral-500">Loading timeline…</p>}
      {[...groups.entries()].map(([day, items]) => (
        <div key={day}>
          <h4 className="mb-1 text-xs font-semibold uppercase text-neutral-500">{day}</h4>
          <ul>{items.map((e) => <TimelineEntry key={e.historyId} entry={e} />)}</ul>
        </div>
      ))}
      {data && data.totalCount > data.pageSize && (
        <div className="flex gap-2">
          <Button size="sm" variant="outline" disabled={page === 1} onClick={() => setPage(page - 1)}>
            Previous
          </Button>
          <Button
            size="sm" variant="outline"
            disabled={page * data.pageSize >= data.totalCount}
            onClick={() => setPage(page + 1)}
          >
            Next
          </Button>
        </div>
      )}
    </div>
  );
}
```

```tsx
// features/node-management/nodes/components/NodeLifecyclePanel.tsx
import { useNodeState } from '../../../../shared/hooks/useNodeLifecycle';
import { NodeStatusSummary } from '../../../../shared/components/node';
import { LifecycleHistoryTimeline } from './LifecycleHistoryTimeline';
import { formatRelativeTime } from '../../../../shared/utils/date';

export function NodeLifecyclePanel({ nodeId }: { nodeId: string }) {
  const { data: state, isLoading } = useNodeState(nodeId);
  if (isLoading || !state) return <p className="p-4 text-sm text-neutral-500">Loading node state…</p>;

  return (
    <div className="space-y-4 p-4">
      <div className="space-y-2">
        <NodeStatusSummary
          lifecycle={state.lifecycleState}
          connectivity={state.connectivityStatus}
          connectivityReason={state.connectivityReason}
          maintenance={state.maintenanceMode}
          maintenanceReason={state.maintenanceReason}
        />
        <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
          <dt className="text-neutral-500">Last heartbeat</dt>
          <dd>{state.lastHeartbeatUtc ? formatRelativeTime(state.lastHeartbeatUtc) : '—'}</dd>
          <dt className="text-neutral-500">Last probe</dt>
          <dd>{state.lastProbeUtc ? formatRelativeTime(state.lastProbeUtc) : '—'}</dd>
          {state.maintenanceMode && (
            <>
              <dt className="text-neutral-500">Maintenance until</dt>
              <dd>{state.maintenanceUntil ? new Date(state.maintenanceUntil).toLocaleString() : 'open-ended'}</dd>
            </>
          )}
        </dl>
        {state.decommissionInProgress && (
          <div className="space-y-1">
            <div className="flex justify-between text-xs text-neutral-500">
              <span>Draining…</span>
              <span>
                {state.drainProgressPercent !== null ? `${state.drainProgressPercent}%` : 'in progress'}
                {state.decommissionGraceUntil &&
                  ` · grace until ${new Date(state.decommissionGraceUntil).toLocaleString()}`}
              </span>
            </div>
            <div className="h-2 w-full rounded bg-neutral-200 dark:bg-neutral-800">
              <div
                className="h-2 rounded bg-purple-500 transition-all"
                style={{ width: `${state.drainProgressPercent ?? 5}%` }}
              />
            </div>
          </div>
        )}
      </div>
      <div>
        <h3 className="mb-2 text-sm font-semibold">Lifecycle timeline</h3>
        <LifecycleHistoryTimeline nodeId={nodeId} />
      </div>
    </div>
  );
}
```

```tsx
// features/node-management/nodes/components/NodesGrid.tsx — the three-badge grid
```

Build it from the AG Grid setup in `features/nodes/NodesGrid.tsx` (same grid component, theming, defaultColDef). Data source: `getAllNodes()` from `shared/api/nodes.ts` with `useQuery({ queryKey: ['nodes'] , queryFn: ... })`. Column set (spec §11.2 — Status column replaced by three):

```typescript
{ field: 'nodeName',  headerName: 'Name',     width: 160 },
{ field: 'externalId', headerName: 'External ID', width: 160 },
{ field: 'groupId',   headerName: 'Group',    width: 120 },
{
  field: 'lifecycleState', headerName: 'Lifecycle', width: 170,
  cellRenderer: (p) => p.data ? LifecycleBadge({ state: p.data.lifecycleState }) : null,
},
{
  field: 'connectivityStatus', headerName: 'Connectivity', width: 150,
  cellRenderer: (p) => p.data ? ConnectivityBadge({ status: p.data.connectivityStatus }) : null,
},
{
  field: 'maintenanceMode', headerName: 'Maintenance', width: 130,
  cellRenderer: (p) => p.data ? MaintenanceBadge({ active: p.data.maintenanceMode }) : null,
},
{ field: 'lastHeartbeat', headerName: 'Last Heartbeat', width: 150,
  valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : '—') },
{
  headerName: 'Actions', width: 80, sortable: false,
  cellRenderer: (p) => p.data
    ? <NodeActionsMenu nodeId={p.data.nodeId} canManage={canManage} onAction={onAction} />
    : null,
},
```

(AG Grid cell renderers here return JSX for `NodeActionsMenu` because it is stateful — if the project's grid renders components via plain function calls only, register it as a proper `cellRenderer` React component per AG Grid 35 React support, as done anywhere else in the codebase that renders interactive cells; `features/nodes/columns.ts` calls `ActionMenu({...})` as a function — follow whichever pattern compiles with hooks. `NodeActionsMenu` uses hooks → MUST be a React component cell renderer: `cellRenderer: NodeActionsMenuCell` with `params` props — implement a small wrapper component `NodeActionsMenuCell(params: ICellRendererParams<NodeDto>)`.)

Grid extras:
- **Decommissioned hidden by default**: client-side filter `nodes.filter(n => includeDecommissioned || n.lifecycleState !== 'Decommissioned')` + checkbox "Include decommissioned" above the grid.
- Row click → selects node → `NodeLifecyclePanel` in a right-hand panel (same split layout as `RegistrationsTab`'s queue + `RegistrationDetailPanel`).

**Action dispatch** in `NodesGrid` (metadata decides the surface — no per-state logic):

```tsx
const [dialog, setDialog] = useState<
  | { kind: 'confirm'; nodeId: string; action: TransitionActionDto }
  | { kind: 'maintenance'; nodeId: string }
  | { kind: 'decommission'; nodeId: string; nodeName: string }
  | null
>(null);

const onAction = (nodeId: string, action: TransitionActionDto) => {
  if (action.action === 'Decommission') {
    setDialog({ kind: 'decommission', nodeId, nodeName: nodeNameOf(nodeId) });
  } else if (action.action === 'StartMaintenance') {
    setDialog({ kind: 'maintenance', nodeId });
  } else if (action.requiresConfirmation) {
    setDialog({ kind: 'confirm', nodeId, action });
  } else {
    execute(nodeId, action.action);   // EndMaintenance — no confirmation per metadata
  }
};

// execute maps LifecycleAction → the Task 5 mutation hooks:
// Enable → useEnableNode, Disable → useDisableNode, EndMaintenance → useEndMaintenance,
// ForceCompleteDecommission → useForceCompleteDecommission.
// The confirm dialog uses ConfirmDialog with variant =
//   action.dangerLevel === 'Critical' ? 'destructive' : 'default'.
```

`NodesTab.tsx`: replace the stub with `<NodesGrid />` (+ selected-node `NodeLifecyclePanel`), gate `canManage = useHasPermission(PermissionKeys.ManageNodeLifecycle)`.

- [ ] **Step 5: Recovery review (spec §11.5)**

`RegistrationQueue.tsx`: rows with `registrationType === 'Recovery'` render a distinct badge — reuse `LifecycleBadge` styling conventions with a `LifeBuoy` icon chip labelled "Recovery" (amber/orange), next to the node name.

`RegistrationDetailPanel.tsx`: when `detail.registrationType === 'Recovery'`, render ABOVE the existing `DiffViewer`:

```tsx
{detail.registrationType === 'Recovery' && (
  <div className="mb-3 rounded border border-orange-300 bg-orange-50 p-3 text-sm dark:border-orange-800 dark:bg-orange-950/30">
    <p className="font-medium">Identity recovery request</p>
    <p className="mt-1">
      A node with a known External ID re-registered. Approving revokes ALL existing
      credentials for this node and issues a new one-time bootstrap token; the node
      re-activates before returning to service. Rejecting returns the node to its
      previous lifecycle state.
    </p>
    <CurrentNodeContext externalId={detail.nodeExternalId} />
  </div>
)}
```

`CurrentNodeContext`: small inline component fetching `useNodeState` for the node behind the ExternalId and rendering `NodeStatusSummary` — the node id is resolvable from the registration detail (check `RegistrationDetailDto` for the current node's id/metadata; if only ExternalId is present, look the node up via the nodes list already in cache, or extend `nodeManagementApi`'s registration detail — prefer whatever the DTO already carries).

**Recovery approve returns a bootstrap token** (Task 2 change): update `useApproveRegistration` hook + `nodeManagementApi` approve function to read the response body `{ registrationId, bootstrapToken }`; when `bootstrapToken` is non-null show the existing one-time-token presentation used by the provision wizard's `Step5Complete` (copy-to-clipboard, "shown only once" warning) in a dialog. Bulk approve: rows with `RequiresIndividualApproval` outcome get a toast explaining recovery approvals are individual.

- [ ] **Step 6: Topology recolor (spec §11.2)**

`features/topology/graph/constants.ts`: add lifecycle meta alongside `CONNECTIVITY_META`:

```typescript
export const LIFECYCLE_META: Record<string, { label: string; border: string; icon: string }> = {
  Active:              { label: 'Active',              border: 'border-green-500',   icon: '●' },
  Recovery:            { label: 'Recovery',            border: 'border-orange-500',  icon: '◐' },
  Disabled:            { label: 'Disabled',            border: 'border-neutral-400', icon: '○' },
  Decommissioning:     { label: 'Decommissioning',     border: 'border-purple-500',  icon: '◍' },
  Decommissioned:      { label: 'Decommissioned',      border: 'border-neutral-300', icon: '◌' },
  PendingApproval:     { label: 'Pending Approval',    border: 'border-yellow-500',  icon: '◔' },
  PendingRegistration: { label: 'Pending Registration',border: 'border-blue-500',    icon: '◔' },
  Rejected:            { label: 'Rejected',            border: 'border-red-500',     icon: '✕' },
};
```

`TopologyGroupNode.tsx`: node card border colors by lifecycle (`LIFECYCLE_META[...].border`); the existing connectivity dot stays as the connectivity ring; add the icon glyph + label text beside the dot so state is never color-only. If the topology graph DTO does not yet carry `lifecycleState` per node/group, check what Task 1's `TopologyQueryService` projection now emits (`CanSynchronize` + whatever status fields) and surface `lifecycleState` through the topology DTO + type (backend projection edit is in-scope here if Task 1 didn't already expose it — one field addition in `TopologyQueryService` + `TopologyGroupDto`/graph DTO + `shared/types` mirror).

- [ ] **Step 7: Legacy frontend cleanup**

- `shared/api/nodes.ts`: delete `enableNode`, `disableNode`, `approveRegistration` (routes were deleted in Task 2; the lifecycle equivalents live in `shared/api/lifecycle.ts`).
- `features/nodes/mutations.ts`: delete `useEnableNodeMutation`, `useDisableNodeMutation`, `useApproveRegistrationMutation`.
- `features/nodes/columns.ts` + `NodesPage.tsx`: remove Enable/Disable/Approve action items and the confirm wiring; keep Edit + Create (legacy admin CRUD; the `/nodes` route already redirects to `/node-management` — the page remains only as the CRUD surface if still linked; if nothing routes to `NodesPage` anymore, delete `NodesPage.tsx`, `NodesGrid.tsx` (legacy one), and the dead imports instead — verify with the router and sidebar).
- Search the frontend for any remaining legacy status literals (`'REGISTERED'`, `'OFFLINE'`, `'PROVISIONED'`, `nodeStatusVariant`) — `shared/utils/status.ts`'s `nodeStatusVariant` loses its callers; delete the function when the last caller is gone.

- [ ] **Step 8: Test + build + manual acceptance**

```pwsh
cd src/MSOSync.Frontend
npm run test
npm run build
```

Expected: all green, zero TS errors.

Manual acceptance (dev server against local backend, spec §15 "operator drives complete lifecycle from UI"):

```text
✓ Nodes tab shows Lifecycle / Connectivity / Maintenance badges
✓ Action menu contents change with node state (no action on Decommissioned)
✓ Disable → confirm dialog → badge flips (SignalR, no refresh)
✓ Start maintenance → reason required → Maintenance badge appears
✓ Decommission wizard → typed confirmation → Decommissioning badge + drain bar
✓ Include-decommissioned filter reveals hidden rows
✓ Timeline shows migration seed row + new transitions with day grouping
✓ Recovery registration shows context panel above diff viewer
✓ Topology nodes recolored by lifecycle with icon + label
```

- [ ] **Step 9: Commit**

```pwsh
git add src/MSOSync.Frontend/src/features/node-management src/MSOSync.Frontend/src/features/topology src/MSOSync.Frontend/src/features/nodes src/MSOSync.Frontend/src/shared
git commit -m "feat(12B-1): lifecycle UI — three-badge grid, transitions-driven actions, maintenance dialog, decommission wizard, history timeline, recovery review, topology recolor"
```

(Stage deletions of any removed legacy files with `git rm` by name.)
