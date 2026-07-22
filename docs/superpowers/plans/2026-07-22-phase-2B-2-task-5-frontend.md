# Task 5 — Frontend

**Files:**
- Create: `src/MSOSync.Frontend/src/shared/types/replay.ts`
- Create: `src/MSOSync.Frontend/src/shared/api/replay.ts`
- Create: `src/MSOSync.Frontend/src/shared/hooks/useReplayOperations.ts`
- Create: `src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayWizard.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayDetailPanel.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/jobs/components/__tests__/ReplayWizard.test.tsx`
- Modify: `src/MSOSync.Frontend/src/features/operations/jobs/JobsPage.tsx`
- Modify: `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`

**Interfaces:**
- Consumes from Task 4: `/api/v1/operations/replay` endpoints
- Produces: `ReplayWizard`, `ReplayDetailPanel`, wired into `JobsPage`

---

- [ ] **Step 1: Create types**

```typescript
// src/MSOSync.Frontend/src/shared/types/replay.ts
export type ReplayMode = 'FailedDelivery' | 'MissedData' | 'Both';

export type ReplayItemStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed' | 'Skipped';

export interface CreateReplayOperationRequest {
  nodeId: string;
  replayMode: ReplayMode;
  fromTime: string;    // ISO datetime
  toTime: string;      // ISO datetime
  channelIds?: string[] | null;
  batchIds?: number[] | null;
}

export interface ReplayOperationCreatedDto {
  operationId: string;
  itemCount: number;
}

export interface ReplayOperationDetailDto {
  operationId: string;
  status: string;
  result?: string | null;
  nodeId: string;
  replayMode: ReplayMode;
  fromTime: string;
  toTime: string;
  channelIds?: string[] | null;
  batchIds?: number[] | null;
  totalItems: number;
  completedItems: number;
  failedItems: number;
  skippedItems: number;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface ReplayItemDto {
  itemId: string;
  nodeId: string;
  channelId: string;
  eventCount: number;
  status: ReplayItemStatus;
  errorMessage?: string | null;
  sourceBatchId?: number | null;
  replayBatchId?: number | null;
}

export interface ReplayItemPage {
  items: ReplayItemDto[];
  nextCursor?: string | null;
  hasMore: boolean;
  totalCount?: number | null;
}
```

- [ ] **Step 2: Create API functions**

```typescript
// src/MSOSync.Frontend/src/shared/api/replay.ts
import client from './client';
import type {
  CreateReplayOperationRequest,
  ReplayOperationCreatedDto,
  ReplayOperationDetailDto,
  ReplayItemPage,
} from '../types/replay';

export const replayKeys = {
  all:    ['replay-operations'] as const,
  detail: (id: string) => ['replay-operations', id] as const,
  items:  (id: string) => ['replay-operations', id, 'items'] as const,
};

export async function createReplay(
  body: CreateReplayOperationRequest,
): Promise<ReplayOperationCreatedDto> {
  const { data } = await client.post<ReplayOperationCreatedDto>('/operations/replay', body);
  return data;
}

export async function getReplayDetail(
  id: string, options?: { signal?: AbortSignal },
): Promise<ReplayOperationDetailDto> {
  const { data } = await client.get<ReplayOperationDetailDto>(
    `/operations/replay/${encodeURIComponent(id)}`, options);
  return data;
}

export async function getReplayItems(
  id: string, params?: { status?: string; cursor?: string; pageSize?: number },
  options?: { signal?: AbortSignal },
): Promise<ReplayItemPage> {
  const { data } = await client.get<ReplayItemPage>(
    `/operations/replay/${encodeURIComponent(id)}/items`,
    { params, ...options });
  return data;
}

export async function cancelReplay(id: string): Promise<void> {
  await client.post(`/operations/replay/${encodeURIComponent(id)}/cancel`);
}
```

- [ ] **Step 3: Create hooks**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useReplayOperations.ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  cancelReplay, createReplay, getReplayDetail, getReplayItems, replayKeys,
} from '../api/replay';
import { operationKeys } from '../api/operations';
import type { CreateReplayOperationRequest } from '../types/replay';
import { getErrorMessage } from '../utils/error';

export function useReplayOperation(id: string | null) {
  return useQuery({
    queryKey: replayKeys.detail(id ?? ''),
    queryFn:  ({ signal }) => getReplayDetail(id!, { signal }),
    enabled:  id !== null,
    refetchInterval: 5_000,
  });
}

export function useReplayItems(id: string | null) {
  return useQuery({
    queryKey: replayKeys.items(id ?? ''),
    queryFn:  ({ signal }) => getReplayItems(id!, undefined, { signal }),
    enabled:  id !== null,
    refetchInterval: 5_000,
  });
}

export function useCreateReplay() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateReplayOperationRequest) => createReplay(body),
    onSuccess: (data) => {
      toast.success(`Replay started — ${data.itemCount} item(s) queued`);
      void qc.invalidateQueries({ queryKey: replayKeys.all });
      void qc.invalidateQueries({ queryKey: operationKeys.all });
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  });
}

export function useCancelReplay() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => cancelReplay(id),
    onSuccess: () => {
      toast.success('Replay cancelled');
      void qc.invalidateQueries({ queryKey: replayKeys.all });
      void qc.invalidateQueries({ queryKey: operationKeys.all });
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  });
}
```

- [ ] **Step 4: Create `ReplayWizard`**

```tsx
// src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayWizard.tsx
import { useState } from 'react';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { Textarea } from '@/components/ui/textarea';
import { useCreateReplay } from '@/shared/hooks/useReplayOperations';
import type { ReplayMode } from '@/shared/types/replay';

interface Props { open: boolean; onOpenChange: (open: boolean) => void }

type Step = 1 | 2 | 3 | 4;

export function ReplayWizard({ open, onOpenChange }: Props) {
  const [step, setStep]           = useState<Step>(1);
  const [mode, setMode]           = useState<ReplayMode>('FailedDelivery');
  const [nodeId, setNodeId]       = useState('');
  const [fromTime, setFromTime]   = useState('');
  const [toTime, setToTime]       = useState('');
  const [batchIdsText, setBatchIdsText] = useState('');
  const [rangeError, setRangeError]     = useState('');

  const createMutation = useCreateReplay();

  const validateRange = () => {
    if (!fromTime || !toTime) return false;
    const from = new Date(fromTime);
    const to   = new Date(toTime);
    if (from >= to) { setRangeError('From must be before To'); return false; }
    const days = (to.getTime() - from.getTime()) / 86400000;
    if (days > 90) { setRangeError('Range cannot exceed 90 days'); return false; }
    setRangeError('');
    return true;
  };

  const handleNext = () => {
    if (step === 3 && !validateRange()) return;
    setStep((s) => Math.min(s + 1, 4) as Step);
  };

  const handleSubmit = () => {
    const batchIds = mode === 'FailedDelivery' && batchIdsText.trim()
      ? batchIdsText.split(',').map((s) => parseInt(s.trim(), 10)).filter((n) => !isNaN(n))
      : undefined;

    createMutation.mutate(
      { nodeId, replayMode: mode, fromTime, toTime, batchIds: batchIds ?? null },
      { onSuccess: () => { onOpenChange(false); resetForm(); } },
    );
  };

  const resetForm = () => {
    setStep(1); setMode('FailedDelivery'); setNodeId('');
    setFromTime(''); setToTime(''); setBatchIdsText(''); setRangeError('');
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) resetForm(); onOpenChange(o); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>New Replay — Step {step} of 4</DialogTitle>
        </DialogHeader>

        {step === 1 && (
          <div className="space-y-3">
            <Label>Replay Mode</Label>
            <RadioGroup value={mode} onValueChange={(v) => setMode(v as ReplayMode)}>
              <div className="flex items-center gap-2">
                <RadioGroupItem value="FailedDelivery" id="fd" />
                <Label htmlFor="fd">Failed Delivery — re-queue batches stuck in Error</Label>
              </div>
              <div className="flex items-center gap-2">
                <RadioGroupItem value="MissedData" id="md" />
                <Label htmlFor="md">Missed Data — re-create batches for events node missed</Label>
              </div>
              <div className="flex items-center gap-2">
                <RadioGroupItem value="Both" id="both" />
                <Label htmlFor="both">Both</Label>
              </div>
            </RadioGroup>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3">
            <Label htmlFor="nodeId">Target Node</Label>
            <Input
              id="nodeId"
              placeholder="node-id"
              value={nodeId}
              onChange={(e) => setNodeId(e.target.value)}
            />
          </div>
        )}

        {step === 3 && (
          <div className="space-y-3">
            <div>
              <Label htmlFor="fromTime">From</Label>
              <Input id="fromTime" type="datetime-local" value={fromTime}
                onChange={(e) => setFromTime(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="toTime">To</Label>
              <Input id="toTime" type="datetime-local" value={toTime}
                onChange={(e) => setToTime(e.target.value)} />
            </div>
            {rangeError && <p className="text-sm text-destructive">{rangeError}</p>}
          </div>
        )}

        {step === 4 && (
          <div className="space-y-4">
            <div className="rounded-md border p-3 text-sm space-y-1">
              <div><span className="font-medium">Mode:</span> {mode}</div>
              <div><span className="font-medium">Node:</span> {nodeId}</div>
              <div><span className="font-medium">From:</span> {fromTime}</div>
              <div><span className="font-medium">To:</span> {toTime}</div>
            </div>
            {mode === 'FailedDelivery' && (
              <div>
                <Label htmlFor="batchIds">Batch IDs (optional, comma-separated)</Label>
                <Textarea
                  id="batchIds"
                  placeholder="e.g. 1001, 1002, 1003"
                  value={batchIdsText}
                  onChange={(e) => setBatchIdsText(e.target.value)}
                  rows={3}
                />
              </div>
            )}
          </div>
        )}

        <DialogFooter className="gap-2">
          {step > 1 && (
            <Button variant="outline" onClick={() => setStep((s) => Math.max(s - 1, 1) as Step)}>
              Back
            </Button>
          )}
          {step < 4 ? (
            <Button onClick={handleNext} disabled={step === 2 && !nodeId}>
              Next
            </Button>
          ) : (
            <Button onClick={handleSubmit} disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Starting…' : 'Start Replay'}
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 5: Create `ReplayDetailPanel`**

```tsx
// src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayDetailPanel.tsx
import type { ColDef } from 'ag-grid-community';
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { DataGrid } from '@/shared/components/data-display/DataGrid';
import { useReplayOperation, useReplayItems, useCancelReplay } from '@/shared/hooks/useReplayOperations';
import type { ReplayItemDto } from '@/shared/types/replay';

interface Props { operationId: string; onClose: () => void }

const ITEM_COLS: ColDef<ReplayItemDto>[] = [
  { field: 'channelId',    headerName: 'Channel',      width: 120 },
  { field: 'eventCount',   headerName: 'Events',       width: 80  },
  { field: 'status',       headerName: 'Status',       width: 110 },
  { field: 'sourceBatchId', headerName: 'Source Batch', width: 110 },
  { field: 'replayBatchId', headerName: 'Replay Batch', width: 110 },
  { field: 'errorMessage', headerName: 'Error',        flex: 1    },
];

export function ReplayDetailPanel({ operationId, onClose }: Props) {
  const { data: detail } = useReplayOperation(operationId);
  const { data: items  } = useReplayItems(operationId);
  const cancelMutation   = useCancelReplay();

  const canCancel = detail?.status === 'Running' || detail?.status === 'Pending';
  const progress  = detail && detail.totalItems > 0
    ? Math.round(detail.completedItems * 100 / detail.totalItems)
    : 0;

  return (
    <Sheet open onOpenChange={(o) => { if (!o) onClose(); }}>
      <SheetContent side="right" className="w-[640px] max-w-full overflow-y-auto">
        <SheetHeader>
          <SheetTitle>Batch Replay</SheetTitle>
        </SheetHeader>

        {detail && (
          <div className="mt-4 space-y-4">
            {/* Summary */}
            <div className="rounded-md border p-3 text-sm space-y-1">
              <div><span className="font-medium">Node:</span> {detail.nodeId}</div>
              <div><span className="font-medium">Mode:</span> {detail.replayMode}</div>
              <div><span className="font-medium">Range:</span> {detail.fromTime} → {detail.toTime}</div>
              <div><span className="font-medium">Status:</span> <Badge>{detail.status}</Badge></div>
            </div>

            {/* Progress bar */}
            <div>
              <div className="flex justify-between text-xs mb-1">
                <span>{detail.completedItems}/{detail.totalItems} items</span>
                <span>{progress}%</span>
              </div>
              <div className="h-2 bg-muted rounded overflow-hidden">
                <div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} />
              </div>
            </div>

            {/* Counts */}
            <div className="flex gap-4 text-sm">
              <span className="text-green-600">✓ {detail.completedItems}</span>
              <span className="text-red-600">✗ {detail.failedItems}</span>
              <span className="text-muted-foreground">— {detail.skippedItems}</span>
            </div>

            {canCancel && (
              <Button
                variant="destructive" size="sm"
                onClick={() => cancelMutation.mutate(operationId)}
                disabled={cancelMutation.isPending}
              >
                {cancelMutation.isPending ? 'Cancelling…' : 'Cancel Replay'}
              </Button>
            )}
          </div>
        )}

        {/* Items grid */}
        <div className="mt-4">
          <DataGrid
            rowData={items?.items ?? []}
            columnDefs={ITEM_COLS}
            height={400}
          />
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 6: Write `ReplayWizard` tests**

```tsx
// src/MSOSync.Frontend/src/features/operations/jobs/components/__tests__/ReplayWizard.test.tsx
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReplayWizard } from '../ReplayWizard';

const mockMutate = vi.fn();

vi.mock('@/shared/hooks/useReplayOperations', () => ({
  useCreateReplay: () => ({
    mutate:    mockMutate,
    isPending: false,
  }),
}));

function wrap(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe('ReplayWizard', () => {
  it('renders step 1 mode selection', () => {
    wrap(<ReplayWizard open onOpenChange={() => {}} />);
    expect(screen.getByText(/Replay Mode/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Failed Delivery/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Missed Data/i)).toBeInTheDocument();
  });

  it('advances to step 2 on Next click', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByLabelText(/Target Node/i)).toBeInTheDocument();
  });

  it('hides Batch IDs field when mode is MissedData', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    // Select MissedData
    await user.click(screen.getByLabelText(/Missed Data/i));

    // Advance to step 2, 3, 4
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.type(screen.getByLabelText(/Target Node/i), 'n1');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    // Step 3: fill dates
    const inputs = screen.getAllByRole('textbox');
    // skip datetime-local — just advance
    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(screen.queryByLabelText(/Batch IDs/i)).not.toBeInTheDocument();
  });

  it('shows Batch IDs field when mode is FailedDelivery in step 4', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    // FailedDelivery is default — advance to step 4
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.type(screen.getByLabelText(/Target Node/i), 'n1');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.click(screen.getByRole('button', { name: 'Next' }));

    expect(screen.getByLabelText(/Batch IDs/i)).toBeInTheDocument();
  });

  it('calls createReplay with correct payload on submit', async () => {
    const user = userEvent.setup();
    wrap(<ReplayWizard open onOpenChange={() => {}} />);

    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.type(screen.getByLabelText(/Target Node/i), 'n1');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.click(screen.getByRole('button', { name: 'Next' }));
    await user.click(screen.getByRole('button', { name: 'Start Replay' }));

    expect(mockMutate).toHaveBeenCalledWith(
      expect.objectContaining({ nodeId: 'n1', replayMode: 'FailedDelivery' }),
      expect.any(Object),
    );
  });
});
```

- [ ] **Step 7: Modify `JobsPage.tsx` — add `BatchReplay`**

In `src/MSOSync.Frontend/src/features/operations/jobs/JobsPage.tsx`:

**7a. Add imports** (at top):
```typescript
import { ReplayWizard } from './components/ReplayWizard';
import { ReplayDetailPanel } from './components/ReplayDetailPanel';
```

**7b. Add to `TYPE_BADGE_COLORS`** (after line `RollingUpgrade: ...`):
```typescript
  BatchReplay: 'bg-indigo-100 text-indigo-800',
```

**7c. Add to `ALL_TYPES`** (current line 51):
```typescript
const ALL_TYPES: OperationType[] = ['Export', 'Rollout', 'Decommission', 'RollingMaintenance', 'RollingUpgrade', 'BatchReplay'];
```

Note: `OperationType` is imported from `@/shared/types/operations` — add `'BatchReplay'` to that union type there if it's explicitly typed. Check `src/MSOSync.Frontend/src/shared/types/operations.ts` and add `| 'BatchReplay'` to `OperationType` if needed.

**7d. Add state for replay** (after `wizardOpen` state):
```typescript
const [replayWizardOpen, setReplayWizardOpen] = useState(false);
const [replayDetailId, setReplayDetailId]     = useState<string | null>(null);
```

**7e. Add "New Replay" button** (alongside the existing "New Rolling Operation" button):
```tsx
{canManageLifecycle && (
  <div className="flex gap-2">
    <Button variant="outline" size="sm" onClick={() => setWizardOpen(true)}>
      New Rolling Operation
    </Button>
    <Button variant="outline" size="sm" onClick={() => setReplayWizardOpen(true)}>
      New Replay
    </Button>
  </div>
)}
```

**7f. Update row click handler** (in `onRowClicked`):
```typescript
onRowClicked={(e) => {
  const op = e.data as OperationDto | undefined;
  if (!op) return;
  if (op.operationType === 'RollingMaintenance' || op.operationType === 'RollingUpgrade') {
    setDetailOperationId(op.operationId);
  } else if (op.operationType === 'BatchReplay') {
    setReplayDetailId(op.operationId);
  } else if (op.correlationId) {
    navigate(`/operations/activity?correlationId=${encodeURIComponent(op.correlationId)}`);
  }
}}
```

**7g. Add wizards/panels** (at bottom of JSX, after `RollingOperationWizard`):
```tsx
<ReplayWizard open={replayWizardOpen} onOpenChange={setReplayWizardOpen} />
{replayDetailId && (
  <ReplayDetailPanel
    operationId={replayDetailId}
    onClose={() => setReplayDetailId(null)}
  />
)}
```

- [ ] **Step 8: Update `OperationType` type if needed**

Check `src/MSOSync.Frontend/src/shared/types/operations.ts`:

```
grep -n "OperationType" src/MSOSync.Frontend/src/shared/types/operations.ts
```

If `OperationType` is a string union, add `'BatchReplay'`. If it's `string`, no change needed.

- [ ] **Step 9: Update `eventRouter.ts`**

In the `OperationChanged` case (line 54-59), add replay invalidation:

```typescript
    case OperationsEventType.OperationChanged:
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: operationKeys.all }),
        queryClient.invalidateQueries({ queryKey: ['rolling-operations'] }),
        queryClient.invalidateQueries({ queryKey: ['replay-operations'] }),
      ]);
      return;
```

- [ ] **Step 10: Run frontend tests**

```
cd src/MSOSync.Frontend
npm run test -- ReplayWizard
```

Expected: 5 tests pass.

- [ ] **Step 11: TypeScript type check**

```
cd src/MSOSync.Frontend
npm run type-check
```

Expected: 0 errors.

- [ ] **Step 12: Commit**

```
git add src/MSOSync.Frontend/src/shared/types/replay.ts
git add src/MSOSync.Frontend/src/shared/api/replay.ts
git add src/MSOSync.Frontend/src/shared/hooks/useReplayOperations.ts
git add src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayWizard.tsx
git add src/MSOSync.Frontend/src/features/operations/jobs/components/ReplayDetailPanel.tsx
git add "src/MSOSync.Frontend/src/features/operations/jobs/components/__tests__/ReplayWizard.test.tsx"
git add src/MSOSync.Frontend/src/features/operations/jobs/JobsPage.tsx
git add src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts
git commit -m "feat(2B.2-T5): ReplayWizard + ReplayDetailPanel + JobsPage wiring"
```
