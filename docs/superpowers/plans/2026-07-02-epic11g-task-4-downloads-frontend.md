# Task 4: Downloads Frontend

**Part of:** Epic 11G — Performance & Scale  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11g-performance-scale-design.md`  
**Depends on:** Task 3 (export job backend must exist)

## Files

**Create:**
- `src/MSOSync.Frontend/src/shared/types/export.ts`
- `src/MSOSync.Frontend/src/shared/api/exportJobs.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useExportJobs.ts`
- `src/MSOSync.Frontend/src/features/downloads/DownloadsPage.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/shared/types/index.ts` — re-export `ExportJobDto`, `ExportJobStatus`
- `src/MSOSync.Frontend/src/shared/signalr/types.ts` — add `ExportJobEvent` interface
- `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts` — add `onExportJobEvent` option
- `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts` — add `routeExportJobEvent`
- `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx` — wire `ExportJobEvent` handler
- `src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx` — "All Matching" → create job
- `src/MSOSync.Frontend/src/app/router.tsx` — add `/downloads` route
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add Downloads sidebar item

## Interfaces Consumed (from Task 3)

```
POST   /api/v1/export-jobs  { resourceType, format, filtersJson, parentJobId? } → 202 { jobId }
GET    /api/v1/export-jobs  → ExportJobDto[]
GET    /api/v1/export-jobs/{id}/download  → file stream
DELETE /api/v1/export-jobs/{id}  → 204

SignalR "ExportJobEvent" (delivered to job owner only):
  { jobId: string, status: string, progressPercent: number, rowCount: number | null }

ExportJobStatus values: "Pending" | "Running" | "Completed" | "Failed" | "Deleted" | "Expired"

queryKeys.exportJobs() → ['export-jobs']   (already added in Task 2)
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use `as const`
- All imports relative — no `@/` aliases
- No new npm packages
- `DownloadsPage` is gated by `EXPORT_DATA` permission (hidden from sidebar if lacking, route renders `<PermissionDeniedPage />` inline if missing)

---

- [ ] **Step 1: Read existing files**

Before writing anything, read:
- `src/MSOSync.Frontend/src/shared/types/index.ts` — understand what is re-exported
- `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts` — current `UseSignalROptions` interface (already added `onPermissionEvent`)
- `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx` — how `onPermissionEvent` is wired
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — current `NAV_GROUPS` shape and how `requiredPermission` is used
- `src/MSOSync.Frontend/src/app/router.tsx` — current route definitions
- `src/MSOSync.Frontend/src/features/auth/PermissionGuard.tsx` — the guard component for route wrapping
- `src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx` — already in context from survey
- `src/MSOSync.Frontend/src/shared/hooks/useExport.ts` — how `onExport` currently works for 'all' scope

Pay special attention to how `AppLayout` currently builds the sidebar (the `NAV_GROUPS` array and `permMap` pattern introduced in Task 11F) so you can add Downloads in the right place.

- [ ] **Step 2: Create `shared/types/export.ts`**

```typescript
// src/MSOSync.Frontend/src/shared/types/export.ts

export const ExportJobStatus = {
  Pending:   'Pending',
  Running:   'Running',
  Completed: 'Completed',
  Failed:    'Failed',
  Deleted:   'Deleted',
  Expired:   'Expired',
} as const;

export type ExportJobStatus = (typeof ExportJobStatus)[keyof typeof ExportJobStatus];

export interface ExportJobDto {
  jobId:           string;
  parentJobId:     string | null;
  requestedBy:     string;
  resourceType:    string;
  format:          string;
  status:          ExportJobStatus;
  progressPercent: number;
  rowCount:        number | null;
  errorMessage:    string | null;
  expiresAt:       string | null;
  createdAt:       string;
  startedAt:       string | null;
  completedAt:     string | null;
}

export interface CreateExportJobRequest {
  resourceType: string;
  format:       string;
  filtersJson:  string;
  parentJobId?: string;
}
```

- [ ] **Step 3: Add `ExportJobEvent` to `shared/signalr/types.ts`**

Add to the existing `types.ts` file (after the existing `PermissionEvent` interface):

```typescript
export interface ExportJobEvent {
  jobId:           string;
  status:          string;
  progressPercent: number;
  rowCount:        number | null;
}
```

- [ ] **Step 4: Add `onExportJobEvent` to `useSignalR.ts`**

In `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts`, add to `UseSignalROptions`:

```typescript
onExportJobEvent?: (event: ExportJobEvent) => void;
```

Then inside `startConnection`, register the handler (following the exact same pattern as `onPermissionEvent`):

```typescript
conn.on('ExportJobEvent', (event: ExportJobEvent) => {
  options.onExportJobEvent?.(event);
});
```

Also add `onExportJobEvent` to the `useCallback` deps array (alongside `onEvent` and `onPermissionEvent`):

```typescript
}, [queryClient, onEvent, onPermissionEvent, onExportJobEvent]);
```

- [ ] **Step 5: Create `api/exportJobs.ts`**

```typescript
// src/MSOSync.Frontend/src/shared/api/exportJobs.ts
import client from './client';
import type { ExportJobDto, CreateExportJobRequest } from '../types/export';

export async function createExportJob(request: CreateExportJobRequest): Promise<{ jobId: string }> {
  const { data } = await client.post<{ jobId: string }>('/export-jobs', request);
  return data;
}

export async function getExportJobs(): Promise<ExportJobDto[]> {
  const { data } = await client.get<ExportJobDto[]>('/export-jobs');
  return data;
}

export async function deleteExportJob(jobId: string): Promise<void> {
  await client.delete(`/export-jobs/${jobId}`);
}

export function getDownloadUrl(jobId: string): string {
  return `/api/v1/export-jobs/${jobId}/download`;
}
```

- [ ] **Step 6: Create `useExportJobs` hook**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useExportJobs.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../queryKeys';
import {
  createExportJob,
  getExportJobs,
  deleteExportJob,
} from '../api/exportJobs';
import type { CreateExportJobRequest } from '../types/export';

export function useExportJobs() {
  return useQuery({
    queryKey: queryKeys.exportJobs(),
    queryFn: getExportJobs,
    refetchOnWindowFocus: false,
    staleTime: 30_000,
  });
}

export function useCreateExportJobMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateExportJobRequest) => createExportJob(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.exportJobs() });
    },
  });
}

export function useDeleteExportJobMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (jobId: string) => deleteExportJob(jobId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.exportJobs() });
    },
  });
}
```

- [ ] **Step 7: Add `routeExportJobEvent` to `eventRouter.ts`**

SignalR progress patches: for `Running` status, use `setQueryData` to patch in-place. For terminal statuses (`Completed`, `Failed`, `Deleted`, `Expired`), invalidate:

```typescript
// Add this import at the top of eventRouter.ts:
import type { ExportJobEvent } from './types';
import type { ExportJobDto } from '../types/export';

// Add this function:
export async function routeExportJobEvent(
  queryClient: QueryClient,
  event: ExportJobEvent,
): Promise<void> {
  const terminalStatuses = ['Completed', 'Failed', 'Deleted', 'Expired'];

  if (terminalStatuses.includes(event.status)) {
    // Full refresh on terminal state
    await queryClient.invalidateQueries({ queryKey: ['export-jobs'] });
    return;
  }

  // Patch progress in-place for Running status (smooth progress bar)
  queryClient.setQueryData<ExportJobDto[]>(['export-jobs'], (old) => {
    if (!old) return old;
    return old.map((job) =>
      job.jobId === event.jobId
        ? { ...job, status: event.status, progressPercent: event.progressPercent, rowCount: event.rowCount }
        : job
    );
  });
}
```

- [ ] **Step 8: Wire `ExportJobEvent` in `SignalRProvider.tsx`**

Read `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx`. It currently passes `onPermissionEvent` to `useSignalR`. Add `onExportJobEvent` following the exact same pattern:

```tsx
// Inside SignalRProvider, add alongside the existing onPermissionEvent handler:
const handleExportJobEvent = useCallback((event: ExportJobEvent) => {
  routeExportJobEvent(queryClient, event);
}, [queryClient]);

// Pass to useSignalR:
useSignalR({
  onEvent: handleOperationsEvent,
  onPermissionEvent: handlePermissionEvent,
  onExportJobEvent: handleExportJobEvent,   // ADD THIS
});
```

Import `routeExportJobEvent` and `ExportJobEvent` at the top of `SignalRProvider.tsx`.

- [ ] **Step 9: Create `DownloadsPage.tsx`**

```tsx
// src/MSOSync.Frontend/src/features/downloads/DownloadsPage.tsx
import { Download, Loader2, RefreshCw, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '../../components/ui/button';
import { Badge } from '../../components/ui/badge';
import { Progress } from '../../components/ui/progress';
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '../../components/ui/table';
import { useExportJobs, useDeleteExportJobMutation, useCreateExportJobMutation } from '../../shared/hooks/useExportJobs';
import { getDownloadUrl } from '../../shared/api/exportJobs';
import { ExportJobStatus, type ExportJobDto } from '../../shared/types/export';

function statusVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (status) {
    case ExportJobStatus.Completed: return 'default';
    case ExportJobStatus.Running:   return 'secondary';
    case ExportJobStatus.Failed:    return 'destructive';
    default:                        return 'outline';
  }
}

function formatRelative(iso: string | null): string {
  if (!iso) return '—';
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

export function DownloadsPage() {
  const { data: jobs = [], isLoading } = useExportJobs();
  const { mutate: deleteJob } = useDeleteExportJobMutation();

  function handleDelete(job: ExportJobDto) {
    deleteJob(job.jobId, {
      onSuccess: () => toast.success('Export deleted'),
      onError:   () => toast.error('Failed to delete export'),
    });
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Downloads</h1>
        <span className="text-sm text-muted-foreground">{jobs.length} export{jobs.length !== 1 ? 's' : ''}</span>
      </div>

      {jobs.length === 0 ? (
        <div className="flex flex-col items-center gap-2 p-12 text-muted-foreground">
          <Download className="h-8 w-8" />
          <p>No exports yet. Use the Export menu on any page to queue a download.</p>
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Resource</TableHead>
              <TableHead>Format</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Progress</TableHead>
              <TableHead className="text-right">Rows</TableHead>
              <TableHead>Created</TableHead>
              <TableHead>Completed</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {jobs.map((job) => (
              <TableRow key={job.jobId}>
                <TableCell className="capitalize">{job.resourceType.replace('-', ' ')}</TableCell>
                <TableCell className="uppercase text-xs">{job.format}</TableCell>
                <TableCell>
                  <Badge variant={statusVariant(job.status)}>{job.status}</Badge>
                </TableCell>
                <TableCell className="w-32">
                  {job.status === ExportJobStatus.Running ? (
                    <Progress value={job.progressPercent} className="h-2" />
                  ) : job.status === ExportJobStatus.Completed ? (
                    <Progress value={100} className="h-2" />
                  ) : (
                    <span className="text-muted-foreground text-xs">—</span>
                  )}
                </TableCell>
                <TableCell className="text-right">
                  {job.rowCount?.toLocaleString() ?? '—'}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {formatRelative(job.createdAt)}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {formatRelative(job.completedAt)}
                </TableCell>
                <TableCell>
                  <div className="flex items-center gap-1 justify-end">
                    {job.status === ExportJobStatus.Completed && (
                      <a href={getDownloadUrl(job.jobId)} download>
                        <Button variant="outline" size="sm">
                          <Download className="h-4 w-4" />
                        </Button>
                      </a>
                    )}
                    {job.status === ExportJobStatus.Failed && (
                      <RetryButton job={job} />
                    )}
                    {(job.status === ExportJobStatus.Completed
                      || job.status === ExportJobStatus.Failed) && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => handleDelete(job)}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}

function RetryButton({ job }: { job: ExportJobDto }) {
  const { mutate: createJob, isPending } = useCreateExportJobMutation();

  return (
    <Button
      variant="outline"
      size="sm"
      disabled={isPending}
      onClick={() =>
        createJob(
          {
            resourceType: job.resourceType,
            format:       job.format,
            filtersJson:  '{}',             // ideally re-use stored filters; see note below
            parentJobId:  job.jobId,
          },
          { onSuccess: () => toast.success('Export re-queued') }
        )
      }
    >
      {isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
    </Button>
  );
}
```

Note: The Retry button uses `filtersJson: '{}'` as a placeholder. A real retry should re-use the original `job.filtersJson`. Since `ExportJobDto` doesn't expose `filtersJson` (it's not in the DTO from Task 3), either:
a) Add `filtersJson` to `ExportJobDto` in the controller (simplest)
b) Store it client-side before creating the job  

For 11G, choose option (a): add `FiltersJson` to `ExportJobDto` in `ExportJobController.cs` and `ExportJobDto` record. Then pass `job.filtersJson` to the retry. If `filtersJson` is not available, the retry with `'{}'` still works (creates a full unfiltered export).

- [ ] **Step 10: Update `ExportMenu.tsx` — "All Matching Rows" creates a job**

Read the existing `ExportMenu.tsx` (in context from earlier). Change the "All Matching Rows" `DropdownMenuItem` handlers from the current streaming approach to job creation:

```tsx
// Add import at top:
import { useCreateExportJobMutation } from '../hooks/useExportJobs';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';

// Inside ExportMenu component, add:
const navigate = useNavigate();
const { mutate: createJob, isPending: isCreatingJob } = useCreateExportJobMutation();

function handleQueueExport(format: 'csv' | 'json') {
  createJob(
    {
      resourceType: resource,
      format,
      filtersJson:  JSON.stringify(queryParams),
    },
    {
      onSuccess: () => {
        toast.success('Export queued', {
          description: 'Your download will be ready shortly.',
          action: { label: 'View Downloads', onClick: () => navigate('/downloads') },
        });
      },
      onError: () => toast.error('Failed to queue export'),
    }
  );
}
```

Then change the "All Matching Rows" group items:

```tsx
// Before (calls handle('all', 'csv')):
<DropdownMenuItem onClick={handle('all', 'csv')}>CSV</DropdownMenuItem>
<DropdownMenuItem onClick={handle('all', 'json')}>JSON</DropdownMenuItem>

// After (creates background job):
<DropdownMenuItem onClick={() => handleQueueExport('csv')} disabled={isCreatingJob}>
  {isCreatingJob ? 'Queuing…' : 'CSV'}
</DropdownMenuItem>
<DropdownMenuItem onClick={() => handleQueueExport('json')} disabled={isCreatingJob}>
  {isCreatingJob ? 'Queuing…' : 'JSON'}
</DropdownMenuItem>
```

"Current View" items remain unchanged (client-side, no backend call).

- [ ] **Step 11: Add Downloads route to `router.tsx`**

Read `src/MSOSync.Frontend/src/app/router.tsx`. Find where `/admin/*` routes are defined. Add a new top-level route for `/downloads` (not nested under admin):

```tsx
import { DownloadsPage } from '../features/downloads/DownloadsPage';
import { PermissionGuard } from '../features/auth/PermissionGuard';
import { PermissionKeys } from '../shared/types/permissions';

// Add alongside other top-level routes inside the authenticated layout:
{
  path: 'downloads',
  element: (
    <PermissionGuard requiredPermission={PermissionKeys.ExportData}>
      <DownloadsPage />
    </PermissionGuard>
  ),
},
```

The exact location depends on how the router is structured — follow the existing pattern for adding a new top-level page.

- [ ] **Step 12: Add Downloads to `AppLayout.tsx` sidebar**

Read `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`. Find the `NAV_GROUPS` array (introduced in Epic 11F). Add a Downloads entry between Audit and Administration:

```tsx
// In NAV_GROUPS, after the Audit item and before Administration:
{
  label: 'Downloads',
  href: '/downloads',
  icon: Download,           // import from 'lucide-react'
  requiredPermission: PermissionKeys.ExportData,
},
```

Import `Download` from `lucide-react` at the top of the file if not already imported.

- [ ] **Step 13: Re-export new types from `shared/types/index.ts`**

Add to `src/MSOSync.Frontend/src/shared/types/index.ts`:

```typescript
export type { ExportJobDto, CreateExportJobRequest } from './export';
export { ExportJobStatus } from './export';
```

- [ ] **Step 14: Build — expect zero TypeScript errors**

```pwsh
cd D:\MSOSync\src\MSOSync.Frontend
npm run build 2>&1 | Select-Object -Last 15
```

Expected: clean build, 0 errors. Fix any type errors before proceeding.

Common errors:
- `Progress` component not found — check if it exists in `@/components/ui/progress` (shadcn). If not, install it with `npx shadcn@latest add progress` (since the project already has shadcn configured, this just generates the component file — no new npm package).
- `Badge` component — same check. Already used in other pages, should be available.
- `Table*` components — already used in other pages.
- Missing import for `routeExportJobEvent` — make sure the import path is relative and correct.

- [ ] **Step 15: Commit**

```pwsh
cd D:\MSOSync
git add `
  src/MSOSync.Frontend/src/shared/types/export.ts `
  src/MSOSync.Frontend/src/shared/types/index.ts `
  src/MSOSync.Frontend/src/shared/api/exportJobs.ts `
  src/MSOSync.Frontend/src/shared/hooks/useExportJobs.ts `
  src/MSOSync.Frontend/src/shared/signalr/types.ts `
  src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts `
  src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts `
  src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx `
  src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx `
  src/MSOSync.Frontend/src/shared/queryKeys.ts `
  src/MSOSync.Frontend/src/features/downloads/DownloadsPage.tsx `
  src/MSOSync.Frontend/src/app/router.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx

git commit -m "feat(11g): add Downloads page + ExportMenu job queuing + SignalR progress patches"
```

## Status Report Format

```
Status: DONE
Commit: <sha>
Build: clean (0 TS errors)
Concerns: <none or list>
```
