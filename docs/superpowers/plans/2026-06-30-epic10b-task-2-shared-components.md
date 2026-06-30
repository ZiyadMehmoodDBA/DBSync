# Epic 10B — Task 2: Shared React Components + AG Grid CSS

> Read the master plan first: `docs/superpowers/plans/2026-06-30-epic10b-data-tables.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

**Goal:** Add AG Grid CSS imports and create all shared React components: DataGrid (client-side wrapper), ServerGrid (server-side wrapper with pagination controls), SummaryCard, StatusBadge, ErrorState, EmptyState.

**Files to create/modify** (all under `src/MSOSync.Frontend/src/`):

Modify:
- `src/index.css` — add AG Grid CSS imports at top

Create:
- `shared/components/data-display/DataGrid.tsx`
- `shared/components/data-display/ServerGrid.tsx`
- `shared/components/data-display/SummaryCard.tsx`
- `shared/components/data-display/StatusBadge.tsx`
- `shared/components/feedback/ErrorState.tsx`
- `shared/components/feedback/EmptyState.tsx`

**Interfaces — Consumes (from Task 1):**
- `shared/types/common.ts` → `ApiError`
- shadcn components at `../../components/ui/card`, `../../components/ui/badge`, `../../components/ui/button`
- `../../lib/utils` → `cn`

**Interfaces — Produces (later tasks import from these paths):**
- `import { DataGrid } from '../../shared/components/data-display/DataGrid'`
- `import { ServerGrid } from '../../shared/components/data-display/ServerGrid'`
- `import { SummaryCard } from '../../shared/components/data-display/SummaryCard'`
- `import { StatusBadge } from '../../shared/components/data-display/StatusBadge'`
- `import { ErrorState } from '../../shared/components/feedback/ErrorState'`
- `import { EmptyState } from '../../shared/components/feedback/EmptyState'`

**AG Grid package API (v35.3):**
- `import { AgGridReact } from 'ag-grid-react'` — main grid component
- `import type { ColDef } from 'ag-grid-community'` — column definition type
- `AgGridReact` props: `rowData`, `columnDefs`, `loading` (shows built-in overlay), `quickFilterText`, `pagination`, `paginationPageSize`, `domLayout`

---

- [ ] **Step 1: Add AG Grid CSS imports to `src/index.css`**

Current content of `src/index.css`:
```css
@import "tailwindcss";

@variant dark (&:is(.dark *));
```

New content (add AG Grid imports before Tailwind):
```css
@import "ag-grid-community/styles/ag-grid.css";
@import "ag-grid-community/styles/ag-theme-quartz.css";
@import "tailwindcss";

@variant dark (&:is(.dark *));
```

- [ ] **Step 2: Create `shared/components/feedback/EmptyState.tsx`**

```tsx
interface Props {
  message?: string;
}

export function EmptyState({ message = 'No data found' }: Props) {
  return (
    <div className="flex items-center justify-center py-12 text-sm text-neutral-500 dark:text-neutral-400">
      {message}
    </div>
  );
}
```

- [ ] **Step 3: Create `shared/components/feedback/ErrorState.tsx`**

```tsx
import { Button } from '../../../components/ui/button';
import type { ApiError } from '../../types/common';

interface Props {
  error: unknown;
  onRetry?: () => void;
}

function extractMessage(error: unknown): string {
  if (error !== null && typeof error === 'object' && 'response' in error) {
    const response = (error as { response?: { data?: ApiError } }).response;
    if (response?.data?.detail) return response.data.detail;
    if (response?.data?.title) return response.data.title;
  }
  if (error instanceof Error) return error.message;
  return 'An unexpected error occurred';
}

export function ErrorState({ error, onRetry }: Props) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-12">
      <p className="text-sm text-red-600 dark:text-red-400">{extractMessage(error)}</p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          Retry
        </Button>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Create `shared/components/data-display/StatusBadge.tsx`**

```tsx
import { Badge } from '../../../components/ui/badge';
import { cn } from '../../../lib/utils';
import type { StatusVariant } from '../../utils/status';

interface Props {
  status: string;
  variant: StatusVariant;
}

const variantClass: Record<StatusVariant, string> = {
  success: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400 border-green-200 dark:border-green-800',
  warning: 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-400 border-yellow-200 dark:border-yellow-800',
  danger:  'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400 border-red-200 dark:border-red-800',
  neutral: 'bg-neutral-100 text-neutral-700 dark:bg-neutral-800 dark:text-neutral-300 border-neutral-200 dark:border-neutral-700',
};

export function StatusBadge({ status, variant }: Props) {
  return (
    <Badge className={cn('text-xs font-medium', variantClass[variant])}>
      {status}
    </Badge>
  );
}
```

Note: `StatusVariant` is defined in `shared/utils/status.ts` (Task 1) as `'success' | 'warning' | 'danger' | 'neutral'`.

- [ ] **Step 5: Create `shared/components/data-display/SummaryCard.tsx`**

```tsx
import { Card, CardContent, CardHeader, CardTitle } from '../../../components/ui/card';
import { Skeleton } from '../../../components/ui/skeleton';
import { cn } from '../../../lib/utils';
import type { LucideIcon } from 'lucide-react';

interface Props {
  title: string;
  value: string | number;
  subtitle?: string;
  icon?: LucideIcon;
  variant?: 'default' | 'success' | 'warning' | 'danger';
  loading?: boolean;
}

const borderVariant: Record<NonNullable<Props['variant']>, string> = {
  default: '',
  success: 'border-green-400 dark:border-green-600',
  warning: 'border-yellow-400 dark:border-yellow-600',
  danger:  'border-red-400 dark:border-red-600',
};

export function SummaryCard({ title, value, subtitle, icon: Icon, variant = 'default', loading = false }: Props) {
  return (
    <Card className={cn('border-l-4', borderVariant[variant])}>
      <CardHeader className="pb-1 pt-4 px-4">
        <CardTitle className="flex items-center gap-2 text-sm font-medium text-neutral-500 dark:text-neutral-400">
          {Icon && <Icon className="h-4 w-4" />}
          {title}
        </CardTitle>
      </CardHeader>
      <CardContent className="px-4 pb-4">
        {loading ? (
          <Skeleton className="h-8 w-24" />
        ) : (
          <p className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">{value}</p>
        )}
        {subtitle && !loading && (
          <p className="text-xs text-neutral-500 dark:text-neutral-400 mt-1">{subtitle}</p>
        )}
      </CardContent>
    </Card>
  );
}
```

Note: `Skeleton` component is at `src/MSOSync.Frontend/src/components/ui/skeleton.tsx` (installed in Epic 10A).

- [ ] **Step 6: Create `shared/components/data-display/DataGrid.tsx`**

Client-side AG Grid wrapper. AG Grid handles its own pagination, sorting, and filtering.

```tsx
import { AgGridReact } from 'ag-grid-react';
import type { ColDef } from 'ag-grid-community';
import { ErrorState } from '../feedback/ErrorState';
import { EmptyState } from '../feedback/EmptyState';

interface Props<T extends object> {
  rowData: T[] | undefined;
  columnDefs: ColDef<T>[];
  loading?: boolean;
  height?: string | number;
  quickFilterText?: string;
  error?: unknown;
  onRetry?: () => void;
}

export function DataGrid<T extends object>({
  rowData,
  columnDefs,
  loading = false,
  height = '100%',
  quickFilterText,
  error,
  onRetry,
}: Props<T>) {
  if (error) return <ErrorState error={error} onRetry={onRetry} />;

  const isEmpty = !loading && (rowData?.length ?? 0) === 0;

  return (
    <div className="flex flex-col gap-2">
      <div className="ag-theme-quartz w-full" style={{ height }}>
        <AgGridReact
          rowData={rowData ?? []}
          columnDefs={columnDefs}
          loading={loading}
          quickFilterText={quickFilterText}
          pagination
          paginationPageSize={20}
          defaultColDef={{ sortable: true, filter: true, resizable: true }}
        />
      </div>
      {isEmpty && <EmptyState />}
    </div>
  );
}
```

- [ ] **Step 7: Create `shared/components/data-display/ServerGrid.tsx`**

Server-side pagination wrapper. The parent component owns page/pageSize state and passes them as props. AG Grid has pagination disabled — app renders custom pagination controls below the grid.

```tsx
import { AgGridReact } from 'ag-grid-react';
import type { ColDef } from 'ag-grid-community';
import { ErrorState } from '../feedback/ErrorState';
import { EmptyState } from '../feedback/EmptyState';
import { Button } from '../../../components/ui/button';

interface Props<T extends object> {
  rowData: T[] | undefined;
  columnDefs: ColDef<T>[];
  loading?: boolean;
  total: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  height?: string | number;
  error?: unknown;
  onRetry?: () => void;
}

export function ServerGrid<T extends object>({
  rowData,
  columnDefs,
  loading = false,
  total,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  height = 500,
  error,
  onRetry,
}: Props<T>) {
  if (error) return <ErrorState error={error} onRetry={onRetry} />;

  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const startRow = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const endRow = Math.min(page * pageSize, total);

  return (
    <div className="flex flex-col gap-2">
      <div className="ag-theme-quartz w-full" style={{ height }}>
        <AgGridReact
          rowData={rowData ?? []}
          columnDefs={columnDefs}
          loading={loading}
          defaultColDef={{ sortable: false, filter: false, resizable: true }}
        />
      </div>
      {!loading && total === 0 && <EmptyState />}
      <div className="flex items-center justify-between px-1 text-sm text-neutral-600 dark:text-neutral-400">
        <span>
          {total === 0
            ? 'No results'
            : `Showing ${startRow}–${endRow} of ${total.toLocaleString()}`}
        </span>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1 || loading}
          >
            ← Prev
          </Button>
          <span className="text-xs">
            Page {page} of {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages || loading}
          >
            Next →
          </Button>
          <select
            value={pageSize}
            onChange={(e) => {
              onPageChange(1);
              onPageSizeChange(Number(e.target.value));
            }}
            className="rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 py-1 text-xs"
          >
            <option value={20}>20 / page</option>
            <option value={50}>50 / page</option>
            <option value={100}>100 / page</option>
          </select>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 8: Verify build + lint + tests pass**

Run from `src/MSOSync.Frontend/`:
```
npm run build
```
Expected: exits 0.

```
npm run lint
```
Expected: 0 errors.

```
npm test
```
Expected: 12/12 pass.

**Common issues to watch for:**
- AG Grid `loading` prop may require boolean — `loading={true}` not `loading="true"`
- `Skeleton` import path: `'../../../components/ui/skeleton'`
- `Badge` from shadcn may need variant prop handling — check `src/components/ui/badge.tsx` for available variants

- [ ] **Step 9: Commit**

```
git add src/MSOSync.Frontend/src/index.css
git add src/MSOSync.Frontend/src/shared/components/
git commit -m "feat(10b): add shared React components and AG Grid CSS"
```

---

## Report Contract

Write report to the path specified by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files created/modified (count)
- Build result
- Lint result
- Test result (N/12 pass)
- Any concerns
