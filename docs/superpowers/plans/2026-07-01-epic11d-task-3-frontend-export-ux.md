# Task 3: Frontend Export UX

**Part of:** Epic 11D — Export + Audit Intelligence  
**Spec:** `docs/superpowers/specs/2026-07-01-epic11d-export-audit-intelligence-design.md`  
**Depends on:** Task 1 (backend export endpoints must exist)

## Files

**Create:**
- `src/MSOSync.Frontend/src/shared/api/export.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useExport.ts`
- `src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx`
- `src/MSOSync.Frontend/src/shared/components/ExportFailureDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/features/events/EventsPage.tsx`
- `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx` (export section only — tabs added in Task 4)
- The incoming-batches and outgoing-batches page files (find them by checking `src/MSOSync.Frontend/src/features/incoming-batches/` and `src/MSOSync.Frontend/src/features/outgoing-batches/`)

## Context

### Existing patterns to follow

**`EventsPage.tsx` current structure:**
```typescript
export function EventsPage() {
  const [filter, setFilter] = useState<EventFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Events</h1>
      <EventFilters onFilter={setFilter} />
      <EventsGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

**`AuditPage.tsx` current structure:**
```typescript
export function AuditPage() {
  const [filter, setFilter] = useState<AuditFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Audit Log</h1>
      <AuditFilters onFilter={setFilter} />
      <AuditGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

**API client:** `src/MSOSync.Frontend/src/shared/api/client.ts` — Axios instance pre-configured with base URL `/api/v1` and JWT auth headers.

**`hooks.ts` pattern (from `features/audit/hooks.ts`):**
```typescript
export function useAuditLog(filter: AuditFilter) {
  return useQuery({
    queryKey: queryKeys.auditLog(filter),
    queryFn: () => getAuditLog(filter),
    placeholderData: keepPreviousData,
    refetchOnWindowFocus: false,
  });
}
```

**shadcn components already installed:** `Button`, `Dialog`, `DropdownMenu`, `Select`, `Input` and others — check `src/MSOSync.Frontend/src/components/ui/` for what's available.

## Interface Produced (consumed by Task 4)

```typescript
// ExportMenu is imported and used in AuditPage; Task 4 will NOT change the ExportMenu import
// The AuditPage after Task 3 wraps export in the Log section; Task 4 adds Insights tab around that
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword
- All imports relative (no `@/` aliases)
- No new npm packages
- `useExport`: "All Rows" failure → show `ExportFailureDialog`; no silent page-fetching fallback
- "Current View" export serializes the current TanStack Query cached data (same query key as the grid)
- Small-table pages (Nodes, Channels, Triggers, Routers, Users, Parameters): ExportMenu with `supportsAllRows={false}`

---

- [ ] **Step 1: Create export.ts — download utilities**

```typescript
// src/MSOSync.Frontend/src/shared/api/export.ts
import client from './client';

export type ExportResource = 'events' | 'incoming-batches' | 'outgoing-batches' | 'audit';
export type ExportFormat = 'csv' | 'json';

export async function downloadExport(
  resource: ExportResource,
  format: ExportFormat,
  params: Record<string, string | number | boolean | undefined>,
): Promise<void> {
  const response = await client.get<Blob>(`/${resource}/export`, {
    params: { format, ...params },
    responseType: 'blob',
  });
  const date = new Date().toISOString().split('T')[0];
  triggerDownload(response.data, `${resource}-${date}.${format}`);
}

export function downloadCurrentViewCsv(
  data: Record<string, unknown>[],
  resource: ExportResource,
): void {
  if (data.length === 0) return;
  const headers = Object.keys(data[0]);
  const rows = data.map((row) =>
    headers.map((h) => csvEscape(String(row[h] ?? ''))).join(','),
  );
  const csv = [headers.join(','), ...rows].join('\n');
  triggerDownload(new Blob([csv], { type: 'text/csv' }), `${resource}-view.csv`);
}

export function downloadCurrentViewJson(
  data: Record<string, unknown>[],
  resource: ExportResource,
): void {
  triggerDownload(
    new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }),
    `${resource}-view.json`,
  );
}

function triggerDownload(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

function csvEscape(s: string): string {
  if (s.includes(',') || s.includes('"') || s.includes('\n') || s.includes('\r'))
    return `"${s.replace(/"/g, '""')}"`;
  return s;
}
```

- [ ] **Step 2: Create useExport hook**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useExport.ts
import { useState, useCallback } from 'react';
import {
  downloadExport,
  downloadCurrentViewCsv,
  downloadCurrentViewJson,
  type ExportResource,
  type ExportFormat,
} from '../api/export';

export type ExportScope = 'view' | 'all';

interface UseExportOptions {
  resource: ExportResource;
  currentData: Record<string, unknown>[];
  queryParams: Record<string, string | number | boolean | undefined>;
}

interface UseExportReturn {
  isExporting: boolean;
  showFailureDialog: boolean;
  pendingViewFormat: ExportFormat | null;
  onExport: (scope: ExportScope, format: ExportFormat) => void;
  onRetry: () => void;
  onCloseFailureDialog: () => void;
  onExportCurrentViewFallback: () => void;
}

export function useExport({
  resource,
  currentData,
  queryParams,
}: UseExportOptions): UseExportReturn {
  const [isExporting, setIsExporting] = useState(false);
  const [showFailureDialog, setShowFailureDialog] = useState(false);
  const [lastAllRowsFormat, setLastAllRowsFormat] = useState<ExportFormat>('csv');
  const [pendingViewFormat, setPendingViewFormat] = useState<ExportFormat | null>(null);

  const runAllRowsExport = useCallback(
    async (format: ExportFormat) => {
      setLastAllRowsFormat(format);
      setIsExporting(true);
      try {
        await downloadExport(resource, format, queryParams);
      } catch {
        setPendingViewFormat(format);
        setShowFailureDialog(true);
      } finally {
        setIsExporting(false);
      }
    },
    [resource, queryParams],
  );

  const onExport = useCallback(
    (scope: ExportScope, format: ExportFormat) => {
      if (scope === 'view') {
        if (format === 'csv') downloadCurrentViewCsv(currentData, resource);
        else downloadCurrentViewJson(currentData, resource);
      } else {
        void runAllRowsExport(format);
      }
    },
    [currentData, resource, runAllRowsExport],
  );

  const onRetry = useCallback(() => {
    setShowFailureDialog(false);
    void runAllRowsExport(lastAllRowsFormat);
  }, [lastAllRowsFormat, runAllRowsExport]);

  const onCloseFailureDialog = useCallback(() => {
    setShowFailureDialog(false);
    setPendingViewFormat(null);
  }, []);

  const onExportCurrentViewFallback = useCallback(() => {
    setShowFailureDialog(false);
    if (pendingViewFormat === 'csv') downloadCurrentViewCsv(currentData, resource);
    else downloadCurrentViewJson(currentData, resource);
    setPendingViewFormat(null);
  }, [pendingViewFormat, currentData, resource]);

  return {
    isExporting,
    showFailureDialog,
    pendingViewFormat,
    onExport,
    onRetry,
    onCloseFailureDialog,
    onExportCurrentViewFallback,
  };
}
```

- [ ] **Step 3: Create ExportFailureDialog**

Check `src/MSOSync.Frontend/src/components/ui/` for an existing `dialog.tsx`. If it exists, import from it. If not, use a simple `<div>` modal with a fixed backdrop (unlikely to be missing given shadcn is already set up).

```typescript
// src/MSOSync.Frontend/src/shared/components/ExportFailureDialog.tsx
import { Button } from '../../components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '../../components/ui/dialog';

interface ExportFailureDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onRetry: () => void;
  onExportCurrentView: () => void;
}

export function ExportFailureDialog({
  open,
  onOpenChange,
  onRetry,
  onExportCurrentView,
}: ExportFailureDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Export failed</DialogTitle>
          <DialogDescription>
            The full export could not be completed. You can retry, or download
            only the current view instead.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onExportCurrentView}>
            Export Current View
          </Button>
          <Button onClick={onRetry}>Retry</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

- [ ] **Step 4: Create ExportMenu**

Check `src/MSOSync.Frontend/src/components/ui/` for `dropdown-menu.tsx`. If it exists, import from it. The shadcn DropdownMenu components needed are: `DropdownMenu`, `DropdownMenuContent`, `DropdownMenuGroup`, `DropdownMenuItem`, `DropdownMenuLabel`, `DropdownMenuSeparator`, `DropdownMenuTrigger`.

```typescript
// src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx
import { Download } from 'lucide-react';
import { Button } from '../../components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../components/ui/dropdown-menu';
import { ExportFailureDialog } from './ExportFailureDialog';
import { useExport, type ExportScope } from '../hooks/useExport';
import type { ExportResource, ExportFormat } from '../api/export';

interface ExportMenuProps {
  resource: ExportResource;
  currentData: Record<string, unknown>[];
  queryParams: Record<string, string | number | boolean | undefined>;
  supportsAllRows?: boolean;
}

export function ExportMenu({
  resource,
  currentData,
  queryParams,
  supportsAllRows = true,
}: ExportMenuProps) {
  const {
    isExporting,
    showFailureDialog,
    onExport,
    onRetry,
    onCloseFailureDialog,
    onExportCurrentViewFallback,
  } = useExport({ resource, currentData, queryParams });

  const handle = (scope: ExportScope, format: ExportFormat) => () =>
    onExport(scope, format);

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" disabled={isExporting}>
            <Download className="mr-2 h-4 w-4" />
            {isExporting ? 'Exporting…' : 'Export'}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-48">
          <DropdownMenuLabel>Current View</DropdownMenuLabel>
          <DropdownMenuGroup>
            <DropdownMenuItem onClick={handle('view', 'csv')}>CSV</DropdownMenuItem>
            <DropdownMenuItem onClick={handle('view', 'json')}>JSON</DropdownMenuItem>
          </DropdownMenuGroup>
          {supportsAllRows && (
            <>
              <DropdownMenuSeparator />
              <DropdownMenuLabel>All Matching Rows</DropdownMenuLabel>
              <DropdownMenuGroup>
                <DropdownMenuItem onClick={handle('all', 'csv')}>CSV</DropdownMenuItem>
                <DropdownMenuItem onClick={handle('all', 'json')}>JSON</DropdownMenuItem>
              </DropdownMenuGroup>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <ExportFailureDialog
        open={showFailureDialog}
        onOpenChange={onCloseFailureDialog}
        onRetry={onRetry}
        onExportCurrentView={onExportCurrentViewFallback}
      />
    </>
  );
}
```

- [ ] **Step 5: Verify TypeScript compiles — no errors**

```pwsh
cd src/MSOSync.Frontend
npx tsc --noEmit 2>&1 | Select-Object -Last 15
cd ../..
```

Expected: no errors.

- [ ] **Step 6: Add ExportMenu to EventsPage**

`EventsPage.tsx` currently fetches nothing itself — data lives in `EventsGrid`. TanStack Query deduplicates requests by query key, so calling `useEvents(filter)` in both `EventsGrid` and `EventsPage` causes zero extra network requests (same cache entry).

Replace `EventsPage.tsx` with:

```typescript
// src/MSOSync.Frontend/src/features/events/EventsPage.tsx
import { useState } from 'react';
import type { EventFilter } from '../../shared/types';
import { EventFilters } from './EventFilters';
import { EventsGrid } from './EventsGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useEvents } from './hooks';

const defaultFilter: EventFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function EventsPage() {
  const [filter, setFilter] = useState<EventFilter>(defaultFilter);
  const { data } = useEvents(filter);  // cache-shared with EventsGrid

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Events</h1>
        <ExportMenu
          resource="events"
          currentData={(data?.data ?? []) as Record<string, unknown>[]}
          queryParams={filter as Record<string, string | number | boolean | undefined>}
        />
      </div>
      <EventFilters onFilter={setFilter} />
      <EventsGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

- [ ] **Step 7: Add ExportMenu to incoming-batches page**

Find the page file: check `src/MSOSync.Frontend/src/features/incoming-batches/`. Look for a file like `IncomingBatchesPage.tsx`. Read it to understand its current structure and the hook it uses (likely `useIncomingBatches` from `hooks.ts`).

Apply the same pattern as EventsPage:
1. Import `useIncomingBatches` (or whatever the hook is named) in the page component
2. Call it with the current filter — TanStack deduplicates the request
3. Add `<ExportMenu resource="incoming-batches" currentData={...} queryParams={filter} />` next to the page heading

Example target structure:
```typescript
// After modification
export function IncomingBatchesPage() {
  const [filter, setFilter] = useState<IncomingBatchFilter>(defaultFilter);
  const { data } = useIncomingBatches(filter);   // cache-shared with the grid

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Incoming Batches</h1>
        <ExportMenu
          resource="incoming-batches"
          currentData={(data?.data ?? []) as Record<string, unknown>[]}
          queryParams={filter as Record<string, string | number | boolean | undefined>}
        />
      </div>
      {/* rest of existing content unchanged */}
    </div>
  );
}
```

- [ ] **Step 8: Add ExportMenu to outgoing-batches page**

Find the page file in `src/MSOSync.Frontend/src/features/outgoing-batches/`. Read it to understand its current structure and hook.

Apply the same pattern. The `queryParams` for outgoing batches should include the fields that match the backend `OutgoingBatchExportFilter`:

```typescript
queryParams={{
  nodeId: filter.nodeId ?? undefined,
  channelId: filter.channelId ?? undefined,
  status: filter.status ?? undefined,
} as Record<string, string | number | boolean | undefined>}
```

(Adjust property names to match the actual filter type.)

- [ ] **Step 9: Add ExportMenu to AuditPage (Log section only)**

Task 4 will add tabs to AuditPage. For now, just add ExportMenu. Replace `AuditPage.tsx` with:

```typescript
// src/MSOSync.Frontend/src/features/audit/AuditPage.tsx
import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import { AuditFilters } from './AuditFilters';
import { AuditGrid } from './AuditGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useAuditLog } from './hooks';

const defaultFilter: AuditFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function AuditPage() {
  const [filter, setFilter] = useState<AuditFilter>(defaultFilter);
  const { data } = useAuditLog(filter);   // cache-shared with AuditGrid

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Audit Log</h1>
        <ExportMenu
          resource="audit"
          currentData={(data?.data ?? []) as Record<string, unknown>[]}
          queryParams={filter as Record<string, string | number | boolean | undefined>}
        />
      </div>
      <AuditFilters onFilter={setFilter} />
      <AuditGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

Note: Task 4 will restructure this page to add tabs. The `ExportMenu` will move inside the "Log" tab content. Do NOT pre-build the tab structure here — Task 4 owns that change.

- [ ] **Step 10: Add ExportMenu (Current View only) to the six small-table pages**

These pages load all data client-side, so "All Matching Rows" would be identical to "Current View". Expose only the Current View section by passing `supportsAllRows={false}`.

For each page file listed below, apply the same pattern: import `ExportMenu`, call the existing data hook with the current filter (cache-shared — zero extra requests), and add `<ExportMenu ... supportsAllRows={false} queryParams={{}} />` next to the page heading.

Pages to update (find exact files in `src/MSOSync.Frontend/src/features/`):

| Feature folder | Resource string |
|----------------|-----------------|
| `nodes/` | `"events"` — wait, Nodes has no export endpoint. Use `supportsAllRows={false}` and pass `resource` as a non-routed value. Since `supportsAllRows={false}`, the `resource` prop is never used for network calls — pass `"events"` as a dummy or extend the `ExportResource` type. |

**Correction:** Since `ExportResource` union is `'events' | 'incoming-batches' | 'outgoing-batches' | 'audit'` and small tables never call the backend, the `resource` prop only determines the download file name. Extend `ExportResource` in `export.ts` to include the small-table names:

```typescript
// In src/MSOSync.Frontend/src/shared/api/export.ts — update the type:
export type ExportResource =
  | 'events'
  | 'incoming-batches'
  | 'outgoing-batches'
  | 'audit'
  | 'nodes'
  | 'channels'
  | 'triggers'
  | 'routers'
  | 'users'
  | 'parameters';
```

Then for each small-table page:

```typescript
// Pattern for NodesPage (read the page to find existing data hook):
import { ExportMenu } from '../../shared/components/ExportMenu';

// In component, call existing data hook (it's already called in the page or grid)
// e.g. const { data } = useNodes();
// Then in JSX:
<div className="flex items-center justify-between">
  <h1 className="text-2xl font-semibold">Nodes</h1>
  <ExportMenu
    resource="nodes"
    currentData={(data ?? []) as Record<string, unknown>[]}
    queryParams={{}}
    supportsAllRows={false}
  />
</div>
```

Apply this to: Nodes, Channels, Triggers, Routers, Users, Parameters pages.
For each: read the current page file first, find how it obtains data (likely a direct hook call), then add ExportMenu alongside the heading.

- [ ] **Step 11: TypeScript check and build**

```pwsh
cd src/MSOSync.Frontend
npx tsc --noEmit 2>&1 | Select-Object -Last 15
cd ../..
```

Expected: no errors.

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror 2>&1 | Select-Object -Last 5
```

Expected: build clean.

- [ ] **Step 12: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/shared/api/export.ts `
  src/MSOSync.Frontend/src/shared/hooks/useExport.ts `
  src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx `
  src/MSOSync.Frontend/src/shared/components/ExportFailureDialog.tsx `
  src/MSOSync.Frontend/src/features/events/EventsPage.tsx `
  src/MSOSync.Frontend/src/features/audit/AuditPage.tsx

# Also stage the incoming-batches, outgoing-batches, and small-table page files
git add src/MSOSync.Frontend/src/features/incoming-batches/
git add src/MSOSync.Frontend/src/features/outgoing-batches/
git add src/MSOSync.Frontend/src/features/nodes/
git add src/MSOSync.Frontend/src/features/channels/
git add src/MSOSync.Frontend/src/features/triggers/
git add src/MSOSync.Frontend/src/features/routers/
git add src/MSOSync.Frontend/src/features/users/
git add src/MSOSync.Frontend/src/features/parameters/

git commit -m "feat: add ExportMenu to Events, IncomingBatches, OutgoingBatches, Audit pages

useExport hook streams from backend with failure dialog fallback (Retry / Export Current View).
Current View export serializes TanStack Query cache to CSV or JSON without extra network requests."
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Tests: TypeScript: no errors, Build: clean
Concerns: <none or list>
```
