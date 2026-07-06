# Epic 12A Task 5: Registration Queue Frontend

> **For agentic workers:** This is Task 5 of 7. Task 4 must be complete (stub components and routing exist). This task fills in the registrations tab, the diff viewer, and all hooks/API functions for registrations and overview.

**Goal:** Implement the full `RegistrationsTab` experience: paginated registration queue (left pane), detail panel with diff viewer (right pane), bulk action toolbar, and all TanStack Query hooks. Also implement `OverviewTab` with stats grid.

## Global Constraints

- React 19, TanStack Query v5 — no new npm packages
- No new npm packages — use only what's already installed
- API base path: `api/v1/node-management` (matches the backend controller)
- After approve/reject mutations: invalidate `['node-management', 'registrations']` AND `['node-management', 'overview']` query keys
- Prefetch registration detail on row hover via `queryClient.prefetchQuery`
- `DiffViewer` default view: `'changes'` (only modified/added/removed rows); toggle to `'all'` shows unchanged too
- TypeScript strict mode: no `any`, no unused vars
- shadcn/ui components only (Button, Badge, Separator already installed); Lucide icons

## Files

**Create:**
- `src/MSOSync.Frontend/src/features/node-management/api/nodeManagementApi.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/queryKeys.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useNodeManagementOverview.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useNodeManagementRegistrations.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useRegistrationDetail.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useApproveRegistration.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useRejectRegistration.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useBulkApproveRegistrations.ts`
- `src/MSOSync.Frontend/src/features/node-management/hooks/useBulkRejectRegistrations.ts`
- `src/MSOSync.Frontend/src/features/node-management/shared/components/DiffViewer.tsx`
- `src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationQueue.tsx`
- `src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationDetailPanel.tsx`
- `src/MSOSync.Frontend/src/features/node-management/registrations/components/BulkActionToolbar.tsx`
- `src/MSOSync.Frontend/src/features/node-management/registrations/components/DiffTable.tsx`

**Modify (replace stubs):**
- `src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationsTab.tsx`
- `src/MSOSync.Frontend/src/features/node-management/overview/components/OverviewTab.tsx`

## Interfaces Consumed (from Task 4)

```typescript
// From NodeManagementProvider
useNodeManagement(): {
  activeTab, setActiveTab,
  selectedRegistration, setSelectedRegistration,
  bulkSelection, toggleBulkSelect, clearBulkSelection,
  wizardDraft, setWizardDraft,
}

// From types/registration.ts
RegistrationSummaryDto, RegistrationDetailDto, RegistrationDiffDto,
RegistrationDiffItemDto, RegistrationChangeType, RegistrationListFilter,
CursorPageResult<T>

// From types/provision.ts
NodeManagementOverviewDto
```

---

## Steps

- [ ] **Step 1: Create nodeManagementApi.ts**

```typescript
// src/MSOSync.Frontend/src/features/node-management/api/nodeManagementApi.ts
import client from '../../../shared/api/client';
import type {
  RegistrationSummaryDto,
  RegistrationDetailDto,
  RegistrationListFilter,
  CursorPageResult,
} from '../types/registration';
import type {
  NodeManagementOverviewDto,
  ProvisionRequest,
  ProvisionResult,
  ProvisionPackageRequest,
} from '../types/provision';

const BASE = '/node-management';

export async function getOverview(): Promise<NodeManagementOverviewDto> {
  const { data } = await client.get<NodeManagementOverviewDto>(`${BASE}/overview`);
  return data;
}

export async function getRegistrations(
  filter: RegistrationListFilter,
  signal?: AbortSignal,
): Promise<CursorPageResult<RegistrationSummaryDto>> {
  const { data } = await client.get<CursorPageResult<RegistrationSummaryDto>>(
    `${BASE}/registrations`,
    { params: filter, signal },
  );
  return data;
}

export async function getRegistrationDetail(
  id: number,
  signal?: AbortSignal,
): Promise<RegistrationDetailDto> {
  const { data } = await client.get<RegistrationDetailDto>(
    `${BASE}/registrations/${id}`,
    { signal },
  );
  return data;
}

export async function approveRegistration(
  id: number,
  notes?: string,
): Promise<void> {
  await client.post(`${BASE}/registrations/${id}/approve`, { notes });
}

export async function rejectRegistration(
  id: number,
  reason?: string,
): Promise<void> {
  await client.post(`${BASE}/registrations/${id}/reject`, { reason });
}

export interface BulkResultItem { id: number; status: string }

export async function bulkApproveRegistrations(
  ids: number[],
): Promise<BulkResultItem[]> {
  const { data } = await client.post<BulkResultItem[]>(
    `${BASE}/registrations/bulk-approve`,
    { ids },
  );
  return data;
}

export async function bulkRejectRegistrations(
  ids: number[],
  reason?: string,
): Promise<BulkResultItem[]> {
  const { data } = await client.post<BulkResultItem[]>(
    `${BASE}/registrations/bulk-reject`,
    { ids, reason },
  );
  return data;
}

export async function provision(request: ProvisionRequest): Promise<ProvisionResult> {
  const { data } = await client.post<ProvisionResult>(`${BASE}/provision`, request);
  return data;
}

export async function downloadProvisionPackage(
  request: ProvisionPackageRequest,
): Promise<Blob> {
  const { data } = await client.post<Blob>(`${BASE}/provision-package`, request, {
    responseType: 'blob',
  });
  return data;
}
```

- [ ] **Step 2: Create queryKeys.ts**

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/queryKeys.ts
import type { RegistrationListFilter } from '../types/registration';

export const nodeManagementKeys = {
  overview:           (): readonly unknown[] => ['node-management', 'overview'],
  registrations:      (f: RegistrationListFilter): readonly unknown[] =>
                        ['node-management', 'registrations', f],
  registrationDetail: (id: number): readonly unknown[] =>
                        ['node-management', 'registrations', id],
  nodes:              (): readonly unknown[] => ['node-management', 'nodes'],
  groups:             (): readonly unknown[] => ['node-management', 'groups'],
} as const;
```

- [ ] **Step 3: Create read hooks**

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useNodeManagementOverview.ts
import { useQuery } from '@tanstack/react-query';
import { getOverview } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useNodeManagementOverview() {
  return useQuery({
    queryKey: nodeManagementKeys.overview(),
    queryFn:  getOverview,
    staleTime: 30_000,
  });
}
```

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useNodeManagementRegistrations.ts
import { useQuery } from '@tanstack/react-query';
import { getRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';
import type { RegistrationListFilter } from '../types/registration';

export function useNodeManagementRegistrations(filter: RegistrationListFilter) {
  return useQuery({
    queryKey: nodeManagementKeys.registrations(filter),
    queryFn:  ({ signal }) => getRegistrations(filter, signal),
    staleTime: 15_000,
  });
}
```

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useRegistrationDetail.ts
import { useQuery } from '@tanstack/react-query';
import { getRegistrationDetail } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useRegistrationDetail(id: number | null) {
  return useQuery({
    queryKey: nodeManagementKeys.registrationDetail(id ?? 0),
    queryFn:  ({ signal }) => getRegistrationDetail(id!, signal),
    enabled:  id !== null,
    staleTime: 60_000,
  });
}
```

- [ ] **Step 4: Create mutation hooks**

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useApproveRegistration.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { approveRegistration } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useApproveRegistration() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, notes }: { id: number; notes?: string }) =>
      approveRegistration(id, notes),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
```

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useRejectRegistration.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { rejectRegistration } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useRejectRegistration() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason?: string }) =>
      rejectRegistration(id, reason),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
```

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useBulkApproveRegistrations.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { bulkApproveRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useBulkApproveRegistrations() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ ids }: { ids: number[] }) => bulkApproveRegistrations(ids),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
```

```typescript
// src/MSOSync.Frontend/src/features/node-management/hooks/useBulkRejectRegistrations.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { bulkRejectRegistrations } from '../api/nodeManagementApi';
import { nodeManagementKeys } from './queryKeys';

export function useBulkRejectRegistrations() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ ids, reason }: { ids: number[]; reason?: string }) =>
      bulkRejectRegistrations(ids, reason),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['node-management', 'registrations'] });
      qc.invalidateQueries({ queryKey: nodeManagementKeys.overview() });
    },
  });
}
```

- [ ] **Step 5: Create DiffViewer shared component**

```tsx
// src/MSOSync.Frontend/src/features/node-management/shared/components/DiffViewer.tsx
import { useState } from 'react';
import { cn } from '../../../../lib/utils';
import type { RegistrationDiffItemDto, RegistrationChangeType } from '../../types/registration';

interface DiffViewerProps {
  items:       RegistrationDiffItemDto[];
  defaultView?: 'changes' | 'all';
}

function changeClass(changeType: RegistrationChangeType): string {
  switch (changeType) {
    case 'Modified': return 'bg-amber-50  dark:bg-amber-950/30';
    case 'Added':    return 'bg-green-50  dark:bg-green-950/30';
    case 'Removed':  return 'bg-red-50    dark:bg-red-950/30';
    default:         return 'bg-neutral-50 dark:bg-neutral-900';
  }
}

function ChangeBadge({ changeType }: { changeType: RegistrationChangeType }) {
  const classes: Record<RegistrationChangeType, string> = {
    Modified:  'bg-amber-100  text-amber-800  dark:bg-amber-900/50  dark:text-amber-300',
    Added:     'bg-green-100  text-green-800  dark:bg-green-900/50  dark:text-green-300',
    Removed:   'bg-red-100    text-red-800    dark:bg-red-900/50    dark:text-red-300',
    Unchanged: 'bg-neutral-100 text-neutral-600 dark:bg-neutral-800 dark:text-neutral-400',
  };
  return (
    <span className={cn('rounded px-1.5 py-0.5 text-xs font-medium', classes[changeType])}>
      {changeType}
    </span>
  );
}

export function DiffViewer({ items, defaultView = 'changes' }: DiffViewerProps) {
  const [view, setView] = useState<'changes' | 'all'>(defaultView);

  const visible = view === 'all'
    ? items
    : items.filter(i => i.changeType !== 'Unchanged');

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <p className="text-xs text-neutral-500">
          {visible.length} field{visible.length !== 1 ? 's' : ''}
        </p>
        <button
          onClick={() => setView(v => v === 'changes' ? 'all' : 'changes')}
          className="text-xs text-blue-600 dark:text-blue-400 hover:underline"
        >
          {view === 'changes' ? 'Show All' : 'Only Changed'}
        </button>
      </div>
      <div className="rounded-md border overflow-hidden text-sm">
        <table className="w-full table-fixed">
          <thead>
            <tr className="bg-neutral-100 dark:bg-neutral-800 text-neutral-600 dark:text-neutral-400">
              <th className="text-left px-3 py-2 w-1/4">Field</th>
              <th className="text-left px-3 py-2 w-1/4">Current</th>
              <th className="text-left px-3 py-2 w-1/4">Incoming</th>
              <th className="text-left px-3 py-2 w-1/4">Change</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((item, i) => (
              <tr key={i} className={cn('border-t', changeClass(item.changeType))}>
                <td className="px-3 py-2 font-medium text-neutral-700 dark:text-neutral-300">
                  {item.field}
                </td>
                <td className="px-3 py-2 text-neutral-600 dark:text-neutral-400">
                  {item.currentValue ?? <span className="italic text-neutral-400">—</span>}
                </td>
                <td className="px-3 py-2 text-neutral-800 dark:text-neutral-200">
                  {item.incomingValue ?? <span className="italic text-neutral-400">—</span>}
                </td>
                <td className="px-3 py-2">
                  <ChangeBadge changeType={item.changeType} />
                </td>
              </tr>
            ))}
            {visible.length === 0 && (
              <tr>
                <td colSpan={4} className="px-3 py-4 text-center text-neutral-400 text-xs">
                  No {view === 'changes' ? 'changes' : 'fields'} to display.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Create DiffTable component**

```tsx
// src/MSOSync.Frontend/src/features/node-management/registrations/components/DiffTable.tsx
import { DiffViewer } from '../../shared/components/DiffViewer';
import type { RegistrationDiffDto } from '../../types/registration';

interface DiffTableProps {
  diff: RegistrationDiffDto;
}

export function DiffTable({ diff }: DiffTableProps) {
  return (
    <div className="mt-4">
      <h4 className="text-sm font-medium mb-2 text-neutral-700 dark:text-neutral-300">
        Field Diff
      </h4>
      <DiffViewer items={diff.items} defaultView="changes" />
    </div>
  );
}
```

- [ ] **Step 7: Create BulkActionToolbar**

```tsx
// src/MSOSync.Frontend/src/features/node-management/registrations/components/BulkActionToolbar.tsx
import { Button } from '../../../../components/ui/button';
import { CheckCheck, XCircle, X } from 'lucide-react';
import { useNodeManagement } from '../../NodeManagementProvider';
import { useBulkApproveRegistrations } from '../../hooks/useBulkApproveRegistrations';
import { useBulkRejectRegistrations } from '../../hooks/useBulkRejectRegistrations';
import { toast } from 'sonner';

export function BulkActionToolbar() {
  const { bulkSelection, clearBulkSelection } = useNodeManagement();
  const bulkApprove = useBulkApproveRegistrations();
  const bulkReject  = useBulkRejectRegistrations();

  const count = bulkSelection.size;
  if (count === 0) return null;

  async function handleBulkApprove() {
    await bulkApprove.mutateAsync({ ids: Array.from(bulkSelection) });
    clearBulkSelection();
    toast.success(`Approved ${count} registration${count !== 1 ? 's' : ''}`);
  }

  async function handleBulkReject() {
    await bulkReject.mutateAsync({ ids: Array.from(bulkSelection) });
    clearBulkSelection();
    toast.success(`Rejected ${count} registration${count !== 1 ? 's' : ''}`);
  }

  return (
    <div className="sticky top-0 z-10 flex items-center gap-2 bg-blue-50 dark:bg-blue-950/30 border-b border-blue-200 dark:border-blue-800 px-4 py-2">
      <span className="text-sm font-medium text-blue-700 dark:text-blue-300">
        {count} selected
      </span>
      <Button
        size="sm"
        variant="default"
        onClick={handleBulkApprove}
        disabled={bulkApprove.isPending}
      >
        <CheckCheck className="h-4 w-4 mr-1" />
        Approve {count}
      </Button>
      <Button
        size="sm"
        variant="destructive"
        onClick={handleBulkReject}
        disabled={bulkReject.isPending}
      >
        <XCircle className="h-4 w-4 mr-1" />
        Reject {count}
      </Button>
      <Button size="sm" variant="ghost" onClick={clearBulkSelection}>
        <X className="h-4 w-4" />
      </Button>
    </div>
  );
}
```

- [ ] **Step 8: Create RegistrationQueue**

```tsx
// src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationQueue.tsx
import { useQueryClient } from '@tanstack/react-query';
import { useNodeManagement } from '../../NodeManagementProvider';
import { useNodeManagementRegistrations } from '../../hooks/useNodeManagementRegistrations';
import { nodeManagementKeys } from '../../hooks/queryKeys';
import { getRegistrationDetail } from '../../api/nodeManagementApi';
import { cn } from '../../../../lib/utils';
import type { RegistrationSummaryDto, RegistrationStatus } from '../../types/registration';

const STATUS_COLORS: Record<RegistrationStatus, string> = {
  Pending:  'bg-amber-100 text-amber-700 dark:bg-amber-900/50 dark:text-amber-300',
  Approved: 'bg-green-100 text-green-700 dark:bg-green-900/50 dark:text-green-300',
  Rejected: 'bg-red-100   text-red-700   dark:bg-red-900/50   dark:text-red-300',
};

export function RegistrationQueue() {
  const qc = useQueryClient();
  const { selectedRegistration, setSelectedRegistration, toggleBulkSelect, bulkSelection } =
    useNodeManagement();

  const { data, isLoading, isError } = useNodeManagementRegistrations({
    status:           'Pending',
    includeTotalCount: true,
    pageSize:          100,
  });

  function handleHover(id: number) {
    qc.prefetchQuery({
      queryKey: nodeManagementKeys.registrationDetail(id),
      queryFn:  () => getRegistrationDetail(id),
      staleTime: 60_000,
    });
  }

  if (isLoading) return <div className="p-4 text-sm text-neutral-400">Loading…</div>;
  if (isError)   return <div className="p-4 text-sm text-red-500">Failed to load registrations.</div>;

  const items = data?.items ?? [];

  return (
    <div className="flex flex-col h-full overflow-y-auto">
      <div className="px-3 py-2 text-xs text-neutral-500 border-b dark:border-neutral-800">
        {data?.totalCount ?? items.length} pending
      </div>
      {items.map(r => (
        <div
          key={r.id}
          onMouseEnter={() => handleHover(r.id)}
          onClick={() => setSelectedRegistration(r)}
          className={cn(
            'flex items-start gap-2 px-3 py-3 cursor-pointer border-b dark:border-neutral-800 transition-colors',
            selectedRegistration?.id === r.id
              ? 'bg-blue-50 dark:bg-blue-950/20'
              : 'hover:bg-neutral-50 dark:hover:bg-neutral-800/50',
          )}
        >
          <input
            type="checkbox"
            checked={bulkSelection.has(r.id)}
            onChange={() => toggleBulkSelect(r.id)}
            onClick={e => e.stopPropagation()}
            className="mt-0.5 shrink-0"
          />
          <div className="flex-1 min-w-0">
            <p className="font-medium text-sm truncate">{r.nodeName}</p>
            <p className="text-xs text-neutral-500 truncate">{r.nodeExternalId}</p>
            <div className="flex items-center gap-2 mt-1">
              <span className={cn('rounded px-1.5 py-0.5 text-xs font-medium', STATUS_COLORS[r.status])}>
                {r.status}
              </span>
              <span className="text-xs text-neutral-400">{r.registrationType}</span>
            </div>
          </div>
        </div>
      ))}
      {items.length === 0 && (
        <div className="p-4 text-center text-sm text-neutral-400">No pending registrations.</div>
      )}
    </div>
  );
}
```

- [ ] **Step 9: Create RegistrationDetailPanel**

```tsx
// src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationDetailPanel.tsx
import { useHasPermission } from '../../../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../../../shared/types/permissions';
import { useNodeManagement } from '../../NodeManagementProvider';
import { useRegistrationDetail } from '../../hooks/useRegistrationDetail';
import { useApproveRegistration } from '../../hooks/useApproveRegistration';
import { useRejectRegistration } from '../../hooks/useRejectRegistration';
import { DiffTable } from './DiffTable';
import { Button } from '../../../../components/ui/button';
import { CheckCheck, XCircle } from 'lucide-react';
import { toast } from 'sonner';

export function RegistrationDetailPanel() {
  const { selectedRegistration, setSelectedRegistration } = useNodeManagement();
  const canApprove = useHasPermission(PermissionKeys.ApproveNodes);
  const { data: detail, isLoading } = useRegistrationDetail(selectedRegistration?.id ?? null);
  const approve = useApproveRegistration();
  const reject  = useRejectRegistration();

  if (!selectedRegistration) {
    return (
      <div className="flex items-center justify-center h-full text-sm text-neutral-400">
        Select a registration to view details.
      </div>
    );
  }

  async function handleApprove() {
    await approve.mutateAsync({ id: selectedRegistration!.id });
    toast.success('Registration approved');
    setSelectedRegistration(null);
  }

  async function handleReject() {
    await reject.mutateAsync({ id: selectedRegistration!.id });
    toast.success('Registration rejected');
    setSelectedRegistration(null);
  }

  return (
    <div className="flex flex-col h-full overflow-y-auto p-4 gap-4">
      <div>
        <h3 className="font-semibold text-base">{selectedRegistration.nodeName}</h3>
        <p className="text-sm text-neutral-500">{selectedRegistration.nodeExternalId}</p>
        <div className="flex gap-2 mt-1 text-xs text-neutral-500">
          <span>{selectedRegistration.registrationType}</span>
          <span>·</span>
          <span>{selectedRegistration.status}</span>
        </div>
      </div>

      {canApprove && selectedRegistration.status === 'Pending' && (
        <div className="flex gap-2">
          <Button
            size="sm"
            onClick={handleApprove}
            disabled={approve.isPending}
          >
            <CheckCheck className="h-4 w-4 mr-1" />
            Approve
          </Button>
          <Button
            size="sm"
            variant="destructive"
            onClick={handleReject}
            disabled={reject.isPending}
          >
            <XCircle className="h-4 w-4 mr-1" />
            Reject
          </Button>
        </div>
      )}

      {isLoading && (
        <p className="text-sm text-neutral-400">Loading details…</p>
      )}

      {detail?.diff && <DiffTable diff={detail.diff} />}

      {detail?.metadata && (
        <div className="text-xs text-neutral-600 dark:text-neutral-400">
          <p className="font-medium mb-1">Metadata</p>
          {detail.metadata.machine?.hostName && (
            <p>Host: {detail.metadata.machine.hostName}</p>
          )}
          {detail.metadata.application?.agentVersion && (
            <p>Agent: {detail.metadata.application.agentVersion}</p>
          )}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 10: Replace RegistrationsTab stub**

```tsx
// src/MSOSync.Frontend/src/features/node-management/registrations/components/RegistrationsTab.tsx
import { BulkActionToolbar } from './BulkActionToolbar';
import { RegistrationQueue } from './RegistrationQueue';
import { RegistrationDetailPanel } from './RegistrationDetailPanel';

export function RegistrationsTab() {
  return (
    <div className="flex flex-col h-full">
      <BulkActionToolbar />
      <div className="flex flex-1 overflow-hidden">
        <div className="w-72 shrink-0 border-r dark:border-neutral-800 overflow-y-auto">
          <RegistrationQueue />
        </div>
        <div className="flex-1 overflow-y-auto">
          <RegistrationDetailPanel />
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 11: Replace OverviewTab stub**

```tsx
// src/MSOSync.Frontend/src/features/node-management/overview/components/OverviewTab.tsx
import { useNodeManagementOverview } from '../../hooks/useNodeManagementOverview';
import { StatCard } from './StatCard';

export function OverviewTab() {
  const { data, isLoading, isError } = useNodeManagementOverview();

  if (isLoading) return <div className="p-6 text-sm text-neutral-400">Loading…</div>;
  if (isError)   return <div className="p-6 text-sm text-red-500">Failed to load overview.</div>;
  if (!data)     return null;

  return (
    <div className="p-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4 mb-6">
        <StatCard
          label="Pending Registrations"
          value={data.pendingRegistrations}
          description="Awaiting approval"
        />
        <StatCard
          label="Pending Recoveries"
          value={data.pendingRecoveries}
          description="Recovery requests"
        />
        <StatCard label="Total Nodes"   value={data.totalNodes} />
        <StatCard label="Active Nodes"  value={data.activeNodes} />
        <StatCard label="Offline Nodes" value={data.offlineNodes} />
        <StatCard label="Degraded"      value={data.degradedNodes} />
        <StatCard label="Total Groups"  value={data.totalGroups} />
      </div>
      <p className="text-xs text-neutral-400">
        Generated at {new Date(data.generatedAt).toLocaleString()}
      </p>
    </div>
  );
}
```

- [ ] **Step 12: Verify TypeScript build**

```pwsh
cd src/MSOSync.Frontend
npm run build
```

Expected: Build succeeds with zero TypeScript errors.

- [ ] **Step 13: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/features/node-management/api/ `
  src/MSOSync.Frontend/src/features/node-management/hooks/ `
  src/MSOSync.Frontend/src/features/node-management/shared/ `
  src/MSOSync.Frontend/src/features/node-management/registrations/ `
  src/MSOSync.Frontend/src/features/node-management/overview/
git commit -m "feat(12A): registration queue, diff viewer, overview tab, all query hooks"
```
