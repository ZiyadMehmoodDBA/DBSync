# 2B.1 Task 8 — Frontend: Drain UI, AgentVersion, Rolling Operations

**Files:**
- Modify: node list API DTO on the backend so the grid can show agent version (locate: `grep -rn "hasDbPassword" src/MSOSync.Api src/MSOSync.Metadata` — the record that maps `SyncNode` → node list JSON gains `AgentVersion`)
- Modify: `src/MSOSync.Frontend/src/shared/types/lifecycle.ts` (unions)
- Modify: `src/MSOSync.Frontend/src/shared/types/nodes.ts` (`agentVersion`)
- Modify: `src/MSOSync.Frontend/src/shared/types/operations.ts` (unions)
- Create: `src/MSOSync.Frontend/src/shared/types/rolling.ts`
- Modify: `src/MSOSync.Frontend/src/shared/api/lifecycle.ts` (drain calls)
- Create: `src/MSOSync.Frontend/src/shared/api/rolling.ts`
- Modify: `src/MSOSync.Frontend/src/shared/hooks/useNodeLifecycle.ts` (drain hooks)
- Create: `src/MSOSync.Frontend/src/shared/hooks/useRollingOperations.ts`
- Modify: `src/MSOSync.Frontend/src/shared/components/node/LifecycleBadge.tsx`
- Modify: `src/MSOSync.Frontend/src/features/topology/graph/constants.ts` (`LIFECYCLE_META`)
- Modify: `src/MSOSync.Frontend/src/features/node-management/nodes/components/NodeActionsMenu.tsx` (labels)
- Modify: `src/MSOSync.Frontend/src/features/node-management/nodes/components/NodesGrid.tsx` (execute switch + column)
- Modify: `src/MSOSync.Frontend/src/features/operations/jobs/JobsPage.tsx` (types/statuses/detail panel)
- Modify: `src/MSOSync.Frontend/src/features/operations/jobs/components/OperationStatusBadge.tsx` (`Paused`)
- Create: `src/MSOSync.Frontend/src/features/operations/jobs/components/RollingOperationWizard.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/jobs/components/RollingOperationDetailPanel.tsx`
- Modify: `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts` (rolling invalidation)
- Test: Create `src/MSOSync.Frontend/src/features/operations/jobs/components/__tests__/RollingOperationWizard.test.tsx`
- Test: Modify `src/MSOSync.Frontend/src/shared/components/node/__tests__/badges.test.tsx` (Draining case)

**Interfaces:**
- Consumes: Task 2 endpoints (`POST /node-lifecycle/nodes/{id}/drain`, `.../resume-drain`; transition actions `"StartDrain"`/`"ResumeDrain"`, DangerLevel `"Normal"`), Task 3 (`agentVersion` populated by heartbeat), Task 5 routes (`/operations/rolling` CRUD + `steps/{stepId}/confirm`) and DTO shapes, Task 6 op statuses (`"Paused"` string, `Result` `Success|PartialSuccess`).
- Produces: UI only — no new contracts.

- [ ] **Step 1: Backend node list DTO gains agentVersion**

Find the node list DTO record (`grep -rn "hasDbPassword" src/`) and its mapping site; add `string? AgentVersion` to the record and `node.AgentVersion` at the mapping — property order per file convention. Rebuild: `dotnet build D:\MSOSync\MSOSync.sln` → 0 warnings.

- [ ] **Step 2: Type unions**

`src/MSOSync.Frontend/src/shared/types/lifecycle.ts`:

```ts
export type NodeLifecycleState =
  | 'PendingApproval'
  | 'PendingRegistration'
  | 'Active'
  | 'Recovery'
  | 'Disabled'
  | 'Draining'
  | 'Decommissioning'
  | 'Decommissioned'
  | 'Rejected';

export type LifecycleAction =
  | 'Enable'
  | 'Disable'
  | 'StartMaintenance'
  | 'EndMaintenance'
  | 'StartDrain'
  | 'ResumeDrain'
  | 'Decommission'
  | 'ForceCompleteDecommission';
```

`src/MSOSync.Frontend/src/shared/types/nodes.ts` — add to `NodeDto`:

```ts
  agentVersion?: string;
```

`src/MSOSync.Frontend/src/shared/types/operations.ts`:

```ts
export type OperationStatus = 'Pending' | 'Running' | 'Paused' | 'Completed' | 'Failed' | 'Cancelled';
export type OperationType = 'Export' | 'Rollout' | 'Decommission' | 'Recovery' | 'RollingMaintenance' | 'RollingUpgrade';
```

`src/MSOSync.Frontend/src/shared/types/rolling.ts` (mirrors Task 5 DTOs, camelCase over the wire):

```ts
export type RollingKind = 'RollingMaintenance' | 'RollingUpgrade';
export type WaveAction = 'manual-confirm' | 'auto-window';
export type RollingStepStatus =
  | 'Pending' | 'Draining' | 'InMaintenance' | 'AwaitingVerification'
  | 'Completed' | 'Failed' | 'Skipped';

export interface CreateRollingOperationRequest {
  kind: RollingKind;
  nodeIds: string[];
  waveSize?: number;
  wavePercent?: number;
  gateSoakSeconds: number;
  waveAction: WaveAction;
  windowSeconds?: number;
  targetVersion?: string;
  verificationTimeoutSeconds: number;
}

export interface RollingOperationPolicy {
  waveSize: number | null;
  wavePercent: number | null;
  gateSoakSeconds: number;
  waveAction: WaveAction;
  windowSeconds: number | null;
  targetVersion: string | null;
  verificationTimeoutSeconds: number;
}

export interface RollingStepDto {
  stepId: string;
  nodeId: string;
  waveNumber: number;
  status: RollingStepStatus;
  startedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;
}

export interface RollingOperationDetailDto {
  operationId: string;
  operationType: string;
  status: string;
  result: string | null;
  policy: RollingOperationPolicy;
  steps: RollingStepDto[];
}
```

- [ ] **Step 3: API clients**

`src/MSOSync.Frontend/src/shared/api/lifecycle.ts` — append (same style as `disableNode`):

```ts
export async function drainNode(nodeId: string, reason?: string): Promise<void> {
  await client.post(`${base(nodeId)}/drain`, { reason: reason ?? null });
}

export async function resumeDrain(nodeId: string, reason?: string): Promise<void> {
  await client.post(`${base(nodeId)}/resume-drain`, { reason: reason ?? null });
}
```

`src/MSOSync.Frontend/src/shared/api/rolling.ts`:

```ts
import client from './client';
import type { CreateRollingOperationRequest, RollingOperationDetailDto } from '../types/rolling';

export const rollingKeys = {
  all: ['rolling-operations'] as const,
  detail: (id: string) => ['rolling-operations', id] as const,
};

export async function createRollingOperation(
  body: CreateRollingOperationRequest,
): Promise<{ operationId: string }> {
  const { data } = await client.post<{ operationId: string }>('/operations/rolling', body);
  return data;
}

export async function getRollingOperation(
  id: string, options?: { signal?: AbortSignal },
): Promise<RollingOperationDetailDto> {
  const { data } = await client.get<RollingOperationDetailDto>(
    `/operations/rolling/${encodeURIComponent(id)}`, options);
  return data;
}

export async function pauseRollingOperation(id: string): Promise<void> {
  await client.post(`/operations/rolling/${encodeURIComponent(id)}/pause`);
}

export async function resumeRollingOperation(id: string): Promise<void> {
  await client.post(`/operations/rolling/${encodeURIComponent(id)}/resume`);
}

export async function abortRollingOperation(id: string): Promise<void> {
  await client.post(`/operations/rolling/${encodeURIComponent(id)}/abort`);
}

export async function confirmRollingStep(stepId: string): Promise<void> {
  await client.post(`/operations/rolling/steps/${encodeURIComponent(stepId)}/confirm`);
}
```

(Check `client.ts` baseURL — the other api files omit `/api/v1` prefix, so these paths follow suit.)

- [ ] **Step 4: Hooks**

`src/MSOSync.Frontend/src/shared/hooks/useNodeLifecycle.ts` — append using the existing `lifecycleMutation` factory:

```ts
export const useStartDrain = lifecycleMutation(
  (a: { nodeId: string; reason?: string }) => drainNode(a.nodeId, a.reason),
  'Drain started', (a) => a.nodeId);

export const useResumeDrain = lifecycleMutation(
  (a: { nodeId: string; reason?: string }) => resumeDrain(a.nodeId, a.reason),
  'Drain resumed', (a) => a.nodeId);
```

(add `drainNode, resumeDrain` to the `../api/lifecycle` import list.)

`src/MSOSync.Frontend/src/shared/hooks/useRollingOperations.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  abortRollingOperation, confirmRollingStep, createRollingOperation,
  getRollingOperation, pauseRollingOperation, resumeRollingOperation, rollingKeys,
} from '../api/rolling';
import { operationKeys } from '../api/operations';
import type { CreateRollingOperationRequest } from '../types/rolling';
import { getErrorMessage } from '../utils/error';

export function useRollingOperation(id: string | null) {
  return useQuery({
    queryKey: rollingKeys.detail(id ?? ''),
    queryFn: ({ signal }) => getRollingOperation(id!, { signal }),
    enabled: id !== null,
    refetchInterval: 5_000,   // worker advances every 15s; poll for live wave progress
  });
}

function rollingMutation<TArgs>(fn: (args: TArgs) => Promise<unknown>, successMessage: string) {
  return function useRollingMutation() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: fn,
      onSuccess: () => {
        toast.success(successMessage);
        void qc.invalidateQueries({ queryKey: rollingKeys.all });
        void qc.invalidateQueries({ queryKey: operationKeys.all });
      },
      onError: (error) => toast.error(getErrorMessage(error)),
    });
  };
}

export const useCreateRollingOperation = rollingMutation(
  (body: CreateRollingOperationRequest) => createRollingOperation(body), 'Rolling operation created');
export const usePauseRollingOperation  = rollingMutation(pauseRollingOperation,  'Operation paused');
export const useResumeRollingOperation = rollingMutation(resumeRollingOperation, 'Operation resumed');
export const useAbortRollingOperation  = rollingMutation(abortRollingOperation,  'Operation aborted');
export const useConfirmRollingStep     = rollingMutation(confirmRollingStep,     'Step confirmed');
```

(Verify `operationKeys` export name in `src/MSOSync.Frontend/src/shared/api/operations.ts` — eventRouter already imports it.)

- [ ] **Step 5: Badges + topology + menu labels**

`LifecycleBadge.tsx` META — insert after `Active` (import `Waves` from lucide-react):

```tsx
  Draining:            { icon: Waves,              className: 'bg-cyan-100 text-cyan-800 dark:bg-cyan-900/30 dark:text-cyan-400' },
```

`src/features/topology/graph/constants.ts` `LIFECYCLE_META` — insert after `Active`:

```ts
  Draining:            { label: 'Draining',            border: 'border-cyan-500',    icon: '◒' },
```

`NodeActionsMenu.tsx` LABELS:

```ts
  StartDrain: 'Start Drain',
  ResumeDrain: 'Resume from Drain',
```

`src/shared/components/node/__tests__/badges.test.tsx` — add case following the file's existing per-state assertions:

```tsx
it('renders Draining badge', () => {
  render(<LifecycleBadge state="Draining" />);
  expect(screen.getByText('Draining')).toBeInTheDocument();
});
```

- [ ] **Step 6: NodesGrid — execute switch + Agent Version column**

In `NodesGrid.tsx`:
- Import `useStartDrain, useResumeDrain` and instantiate next to the other mutations (`const startDrainMutation = useStartDrain();` etc.).
- Extend `execute`:

```ts
      case 'StartDrain':  startDrainMutation.mutate({ nodeId }); break;
      case 'ResumeDrain': resumeDrainMutation.mutate({ nodeId }); break;
```

(add both mutations to the `useCallback` dep array and to `isPendingConfirm`.)
- `StartDrain` has `requiresConfirmation: true` → flows through the existing `confirm` dialog path automatically; `ResumeDrain` executes directly. No new dialog code.
- Add column after `maintenanceMode` (before `lastHeartbeat`):

```tsx
    {
      field: 'agentVersion' as const, headerName: 'Agent', width: 110,
      valueFormatter: (p: ValueFormatterParams<NodeDto>) => (p.value as string | undefined) ?? '—',
    },
```

- [ ] **Step 7: OperationStatusBadge — Paused**

Add before the `Failed` branch:

```tsx
  if (status === 'Paused') {
    return (
      <Badge className="bg-amber-100 text-amber-800 border border-amber-200">
        Paused
      </Badge>
    );
  }
```

- [ ] **Step 8: JobsPage — types, statuses, rolling wizard entry, detail panel**

In `JobsPage.tsx`:

```ts
const TYPE_BADGE_COLORS: Record<string, string> = {
  Export:             'bg-violet-100 text-violet-800',
  Rollout:            'bg-blue-100 text-blue-800',
  Decommission:       'bg-orange-100 text-orange-800',
  Recovery:           'bg-teal-100 text-teal-800',
  RollingMaintenance: 'bg-cyan-100 text-cyan-800',
  RollingUpgrade:     'bg-indigo-100 text-indigo-800',
};

const ALL_TYPES: OperationType[] = ['Export', 'Rollout', 'Decommission', 'RollingMaintenance', 'RollingUpgrade'];
const ALL_STATUSES: OperationStatus[] = ['Pending', 'Running', 'Paused', 'Completed', 'Failed', 'Cancelled'];
```

- Header bar: add a "New Rolling Operation" button (right-aligned, `variant="outline"`, gated by `useHasPermission(PermissionKeys.ManageNodeLifecycle)`) opening `RollingOperationWizard`.
- Row click: for `operationType === 'RollingMaintenance' || 'RollingUpgrade'` set `detailOperationId` state (opens `RollingOperationDetailPanel`) instead of navigating to activity:

```ts
onRowClicked={(e) => {
  const op = e.data as OperationDto | undefined;
  if (!op) return;
  if (op.operationType === 'RollingMaintenance' || op.operationType === 'RollingUpgrade') {
    setDetailOperationId(op.operationId);
  } else if (op.correlationId) {
    navigate(`/operations/activity?correlationId=${encodeURIComponent(op.correlationId)}`);
  }
}}
```

- Render `<RollingOperationWizard open={wizardOpen} onOpenChange={setWizardOpen} />` and `{detailOperationId && <RollingOperationDetailPanel operationId={detailOperationId} onClose={() => setDetailOperationId(null)} />}` at the bottom, next to the existing `ConfirmDialog`.

- [ ] **Step 9: RollingOperationWizard**

`src/features/operations/jobs/components/RollingOperationWizard.tsx` — single dialog (use the shared `Dialog`/`Select`/`Button`/`Input` primitives from `@/components/ui`; mirror `MaintenanceDialog` form conventions). Fields and rules (mirror the Task 5 validator so 400s are pre-empted client-side):

- Kind: Select `RollingMaintenance | RollingUpgrade`.
- Nodes: multi-select of Active nodes — `useQuery({ queryKey: ['nodes'], queryFn: getAllNodes })`, filter `lifecycleState === 'Active'`, render checkbox list. Require ≥ 1.
- Wave sizing: radio "By count" (number ≥ 1) vs "By percent" (1–100). Exactly one populated.
- Gate soak seconds: number 0–3600, default 60.
- Wave action: Select `manual-confirm | auto-window`; when `auto-window`, show Window seconds (number > 0, default 600).
- When kind = `RollingUpgrade`: Target version (required non-empty text).
- Verification timeout seconds: number 30–86400, default 900.

Submit → `useCreateRollingOperation().mutate(body, { onSuccess: () => onOpenChange(false) })`. Disable submit while invalid or pending; show field-level messages matching the validator texts (e.g. "WaveSize or WavePercent is required", "TargetVersion is required for RollingUpgrade").

`__tests__/RollingOperationWizard.test.tsx` (vitest + testing-library, follow `DecommissionWizard.test.tsx` setup for QueryClient/provider mocks):

```tsx
it('disables submit until at least one node selected', ...)
it('requires target version for RollingUpgrade', ...)
it('requires window seconds when auto-window selected', ...)
it('submits camelCase payload with selected nodes', ...)  // assert createRollingOperation mock called with expected body
```

- [ ] **Step 10: RollingOperationDetailPanel**

`src/features/operations/jobs/components/RollingOperationDetailPanel.tsx` — right-side panel (mirror the NodesGrid side-panel layout: fixed-width `w-96`, border-l, Close button). Content from `useRollingOperation(operationId)`:

- Header: operation type badge + `OperationStatusBadge` (cast status) + result.
- Policy summary line: wave sizing, wave action, soak, target version (upgrade only).
- Steps grouped by `waveNumber` ascending: `Wave {n}` heading, then one row per step: nodeId, step-status badge (map `RollingStepStatus` → colors: Pending gray, Draining cyan, InMaintenance amber, AwaitingVerification blue, Completed green, Failed red, Skipped neutral), startedAt/completedAt relative times, errorMessage in red when present.
- Step action: when op status `Running` and step `InMaintenance` and policy `waveAction === 'manual-confirm'` → "Confirm" button → `useConfirmRollingStep().mutate(step.stepId)`.
- Operation actions (footer, permission-gated with `PermissionKeys.ManageNodeLifecycle`):
  - `Pause` visible when status `Running`.
  - `Resume` visible when status `Paused`.
  - `Abort` visible when status `Running` or `Paused` — wrap in the shared `ConfirmDialog` (`variant="destructive"`, description "Aborts remaining steps and restores in-flight nodes to Active.").

- [ ] **Step 11: eventRouter — rolling invalidation**

In `eventRouter.ts` `OperationChanged` case, also invalidate rolling detail queries:

```ts
    case OperationsEventType.OperationChanged:
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: operationKeys.all }),
        queryClient.invalidateQueries({ queryKey: ['rolling-operations'] }),
      ]);
      return;
```

(`NodeLifecycleChanged` already invalidates nodes/topology/state — drain transitions covered, no change.)

- [ ] **Step 12: Typecheck, tests, build**

```powershell
cd src/MSOSync.Frontend
npm run test -- --run
npm run build
```

Expected: vitest green (new wizard + badge tests included), `tsc`/vite build clean. Then start dev server and click through: NodesGrid → drain an Active node (confirm dialog, badge flips to Draining, Resume from Drain appears), Jobs → New Rolling Operation wizard validation paths, detail panel renders waves. If the API isn't running, note in the report which paths were verified statically only.

- [ ] **Step 13: Commit**

```powershell
git add src/MSOSync.Api/ src/MSOSync.Frontend/src/shared/ src/MSOSync.Frontend/src/features/
git commit -m "feat(2B.1-T8): drain UI, agent version column, rolling operations wizard + detail panel"
```

(Adjust the `src/MSOSync.Api/` path to the actual node-list DTO file from Step 1; stage files by name.)
