# Task 2: Cursor Pagination — Frontend

**Part of:** Epic 11G — Performance & Scale  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11g-performance-scale-design.md`  
**Depends on:** Task 1 (backend cursor APIs must be deployed)

## Files

**Create:**
- `src/MSOSync.Frontend/src/shared/hooks/useInfiniteEvents.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useInfiniteIncomingBatches.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useInfiniteOutgoingBatches.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useInfiniteAudit.ts`

**Modify:**
- `src/MSOSync.Frontend/src/shared/types/common.ts` — add `CursorPageResult<T>`
- `src/MSOSync.Frontend/src/shared/api/events.ts` — cursor params + `signal`
- `src/MSOSync.Frontend/src/shared/api/batches.ts` — cursor params + `signal`
- `src/MSOSync.Frontend/src/shared/api/audit.ts` — cursor params + `signal`
- `src/MSOSync.Frontend/src/shared/api/nodes.ts` — `pageNumber`/`pageSize` + `signal`
- `src/MSOSync.Frontend/src/shared/queryKeys.ts` — add `eventsInfinite`, `incomingBatchesInfinite`, `outgoingBatchesInfinite`, `auditLogInfinite`, `exportJobs` keys
- `src/MSOSync.Frontend/src/features/events/EventsPage.tsx` — use `useInfiniteEvents`
- `src/MSOSync.Frontend/src/features/events/EventsGrid.tsx` — Load More footer
- `src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesGrid.tsx`
- `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesGrid.tsx`
- `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`
- `src/MSOSync.Frontend/src/features/audit/AuditGrid.tsx`
- `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx` — offset pagination controls

## Interfaces Consumed (from Task 1)

```
GET /api/v1/events?cursor=<token>&pageSize=100
→ { items: EventSummaryDto[], nextCursor: string | null, hasMore: boolean, totalCount: number | null }

Same shape for /api/v1/incoming-batches, /api/v1/outgoing-batches, /api/v1/audit

GET /api/v1/nodes?pageNumber=1&pageSize=50
→ { items: NodeSummaryDto[], page, pageSize, totalCount }  (existing PagedResult shape)
```

## Interfaces Produced (consumed by Tasks 3-4)

```typescript
// queryKeys additions (Tasks 3-4 add exportJobs key to this same file)
queryKeys.eventsInfinite(filter)         → ['events', 'infinite', filter]
queryKeys.incomingBatchesInfinite(filter) → ['incoming-batches', 'infinite', filter]
queryKeys.outgoingBatchesInfinite(filter) → ['outgoing-batches', 'infinite', filter]
queryKeys.auditLogInfinite(filter)       → ['audit', 'infinite', filter]
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use `as const`
- All imports relative — no `@/` aliases
- No new npm packages
- All API functions must accept and forward `signal?: AbortSignal`

---

- [ ] **Step 1: Read existing frontend files**

Before writing anything, read:
- `src/MSOSync.Frontend/src/shared/types/common.ts` — find `PagedResult<T>` definition and `data` field alias
- `src/MSOSync.Frontend/src/features/events/EventsGrid.tsx` — understand current grid structure
- `src/MSOSync.Frontend/src/features/events/hooks.ts` (or wherever `useEvents` is defined) — current hook
- `src/MSOSync.Frontend/src/shared/hooks/usePreferences.ts` — to handle removing `page` from saved preferences
- `src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesGrid.tsx`
- `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`
- `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`

Note how each page uses its `useXxx(filter)` hook and passes data to the grid component.

- [ ] **Step 2: Add `CursorPageResult<T>` to shared types**

In `src/MSOSync.Frontend/src/shared/types/common.ts`, add:

```typescript
export interface CursorPageResult<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
  totalCount: number | null;
}
```

Do NOT remove `PagedResult<T>` — it's still used by Nodes and other endpoints.

- [ ] **Step 3: Update `api/events.ts` — cursor params + signal**

```typescript
// src/MSOSync.Frontend/src/shared/api/events.ts
import client from './client';
import type { EventSummaryDto, EventFilter } from '../types';
import type { CursorPageResult } from '../types/common';

export type CursorEventFilter = Omit<EventFilter, 'page'> & {
  cursor?: string;
  includeTotalCount?: boolean;
};

export async function getEvents(
  filter: CursorEventFilter,
  options?: { signal?: AbortSignal }
): Promise<CursorPageResult<EventSummaryDto>> {
  const { data } = await client.get<CursorPageResult<EventSummaryDto>>('/events', {
    params: filter,
    signal: options?.signal,
  });
  return data;
}
```

Keep any existing `getEventById` function unchanged.

- [ ] **Step 4: Update `api/batches.ts` and `api/audit.ts` — same pattern**

For each of `batches.ts` (incoming + outgoing) and `audit.ts`:
1. Add `CursorXxxFilter = Omit<XxxFilter, 'page'> & { cursor?: string; includeTotalCount?: boolean }`
2. Update the list function to accept `options?: { signal?: AbortSignal }` and pass `signal` to axios
3. Return `CursorPageResult<T>`

Follow the exact same pattern as Step 3 for Events.

- [ ] **Step 5: Update `api/nodes.ts` — add pageNumber/pageSize + signal**

Read `src/MSOSync.Frontend/src/shared/api/nodes.ts`. Update the main nodes list function:

```typescript
export async function getNodes(
  pageNumber = 1,
  pageSize   = 50,
  options?: { signal?: AbortSignal }
): Promise<PagedResult<NodeSummaryDto>> {
  const { data } = await client.get<PagedResult<NodeSummaryDto>>('/nodes', {
    params: { pageNumber, pageSize },
    signal: options?.signal,
  });
  return data;
}
```

`PagedResult<NodeSummaryDto>` is the existing offset type (unchanged shape from the backend).

- [ ] **Step 6: Update `queryKeys.ts` — add infinite query keys**

Add to the `queryKeys` object in `src/MSOSync.Frontend/src/shared/queryKeys.ts`:

```typescript
// Add these inside the queryKeys object:
eventsInfinite:          (filter: Omit<EventFilter, 'page'>) =>
  ['events', 'infinite', filter] as const,
incomingBatchesInfinite: (filter: Omit<IncomingBatchFilter, 'page'>) =>
  ['incoming-batches', 'infinite', filter] as const,
outgoingBatchesInfinite: (filter: Omit<OutgoingBatchFilter, 'page'>) =>
  ['outgoing-batches', 'infinite', filter] as const,
auditLogInfinite:        (filter: Omit<AuditFilter, 'page'>) =>
  ['audit', 'infinite', filter] as const,
exportJobs:              () => ['export-jobs'] as const,
```

These keys start with the same first element as existing keys (`['events']`, etc.) so existing SignalR event router invalidations automatically cover them without changes.

Import type adjustments: if `EventFilter`, `IncomingBatchFilter`, `OutgoingBatchFilter`, `AuditFilter` are already imported at the top of `queryKeys.ts`, no new imports needed.

- [ ] **Step 7: Create `useInfiniteEvents` hook**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useInfiniteEvents.ts
import { useInfiniteQuery } from '@tanstack/react-query';
import { getEvents, type CursorEventFilter } from '../api/events';
import { queryKeys } from '../queryKeys';

export function useInfiniteEvents(filter: CursorEventFilter) {
  return useInfiniteQuery({
    queryKey: queryKeys.eventsInfinite(filter),
    queryFn: ({ pageParam, signal }) =>
      getEvents({ ...filter, cursor: pageParam as string | undefined }, { signal }),
    getNextPageParam: (lastPage) =>
      lastPage.hasMore ? (lastPage.nextCursor ?? undefined) : undefined,
    initialPageParam: undefined as string | undefined,
  });
}
```

- [ ] **Step 8: Create the other 3 infinite hooks — same pattern**

Create `useInfiniteIncomingBatches`, `useInfiniteOutgoingBatches`, `useInfiniteAudit` following the identical pattern as `useInfiniteEvents`. Each imports its own API function + queryKey.

Example for audit (adjust names for batches):

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useInfiniteAudit.ts
import { useInfiniteQuery } from '@tanstack/react-query';
import { getAuditLog, type CursorAuditFilter } from '../api/audit';
import { queryKeys } from '../queryKeys';

export function useInfiniteAudit(filter: CursorAuditFilter) {
  return useInfiniteQuery({
    queryKey: queryKeys.auditLogInfinite(filter),
    queryFn: ({ pageParam, signal }) =>
      getAuditLog({ ...filter, cursor: pageParam as string | undefined }, { signal }),
    getNextPageParam: (lastPage) =>
      lastPage.hasMore ? (lastPage.nextCursor ?? undefined) : undefined,
    initialPageParam: undefined as string | undefined,
  });
}
```

- [ ] **Step 9: Update `EventsPage.tsx` — switch to `useInfiniteEvents`**

The page currently stores `filter.page` in state and preferences. After removing `page` from the filter:

```tsx
// src/MSOSync.Frontend/src/features/events/EventsPage.tsx
// Key changes:
// 1. Replace useEvents with useInfiniteEvents
// 2. Remove page: 1 from initial filter state
// 3. Flatten pages for ExportMenu currentData

import { useInfiniteEvents } from '../../shared/hooks/useInfiniteEvents';
// Remove: import { useEvents } from './hooks';

export function EventsPage() {
  // Remove page from savedFilter — only keep non-page fields
  const savedFilter = usePreference<Omit<EventFilter, 'page'>>(
    PreferenceKeys.eventsFilter, { pageSize: DEFAULT_PAGE_SIZE }
  );
  // ... (keep savedPageSize and setPref logic)

  const [filter, setFilter] = useState<Omit<EventFilter, 'page'>>({
    pageSize: savedPageSize,
  });

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } =
    useInfiniteEvents(filter);

  // Flatten all pages for ExportMenu currentData
  const allItems = data?.pages.flatMap(p => p.items) ?? [];
  const totalLoaded = allItems.length;

  // handleFilterChange: reset cursor by not including it (new filter = new infinite query)
  function handleFilterChange(next: Omit<EventFilter, 'page'>) {
    setFilter(next);
    setPref({ key: PreferenceKeys.eventsFilter, value: next });
    setPref({ key: PreferenceKeys.eventsPageSize, value: next.pageSize });
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Events</h1>
        <ExportMenu
          resource="events"
          currentData={allItems as unknown as Record<string, unknown>[]}
          queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
          canExport={canExport}
        />
      </div>
      <EventFilters onFilter={handleFilterChange} />
      <EventsGrid
        data={allItems}
        totalLoaded={totalLoaded}
        hasMore={hasNextPage ?? false}
        isFetchingMore={isFetchingNextPage}
        onLoadMore={() => fetchNextPage()}
        onFilterChange={handleFilterChange}
      />
    </div>
  );
}
```

Adjust prop names to match what `EventsGrid` currently accepts. The key requirement is passing `onLoadMore`, `hasMore`, and `isFetchingMore` to the grid.

- [ ] **Step 10: Update `EventsGrid.tsx` — add Load More footer**

Read the existing `EventsGrid.tsx`. The current grid receives data from a hook internally or via props. After the change, the grid receives the flattened data array directly plus Load More controls:

Add a footer below the AG Grid:

```tsx
// At the bottom of EventsGrid's return, after the <div className="ag-theme-...">:
{(hasMore || isFetchingMore) && (
  <div className="flex items-center justify-between px-2 py-3 border-t text-sm text-muted-foreground">
    <span>Showing {data.length} results</span>
    <Button
      variant="outline"
      size="sm"
      onClick={onLoadMore}
      disabled={isFetchingMore}
    >
      {isFetchingMore ? (
        <><Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading…</>
      ) : (
        `Load ${pageSize} More`
      )}
    </Button>
  </div>
)}
{!hasMore && data.length > 0 && (
  <div className="px-2 py-2 text-sm text-muted-foreground text-center border-t">
    Showing all {data.length} results
  </div>
)}
```

Import `Loader2` from `lucide-react`. Import `Button` from the shadcn ui path used in other components in the same file or nearby. The exact import path follows existing patterns in the grid files.

- [ ] **Step 11: Apply the same pattern to IncomingBatches, OutgoingBatches, Audit pages + grids**

For each of the three remaining stream pages and grids, repeat Steps 9-10 following the exact same pattern:

- `IncomingBatchesPage.tsx` → use `useInfiniteIncomingBatches`
- `IncomingBatchesGrid.tsx` → Load More footer
- `OutgoingBatchesPage.tsx` → use `useInfiniteOutgoingBatches`
- `OutgoingBatchesGrid.tsx` → Load More footer
- `AuditPage.tsx` → use `useInfiniteAudit`
- `AuditGrid.tsx` → Load More footer

The "Retry All" button in `OutgoingBatchesPage` is unaffected — it operates on a separate mutation, not on the pagination state.

- [ ] **Step 12: Update `NodesPage.tsx` — add offset pagination controls**

Read the current `NodesPage.tsx`. It currently fetches all nodes without pagination. Add `pageNumber` state and simple Previous/Next controls:

```tsx
const [pageNumber, setPageNumber] = useState(1);
const pageSize = 50;

// Use a regular useQuery (not infinite) since Nodes is management, not a stream:
const { data } = useQuery({
  queryKey: ['nodes', pageNumber, pageSize],
  queryFn: ({ signal }) => getNodes(pageNumber, pageSize, { signal }),
});

// In JSX, below the grid:
<div className="flex items-center justify-between px-2 py-3 border-t text-sm text-muted-foreground">
  <span>
    Showing {((pageNumber - 1) * pageSize) + 1}–
    {Math.min(pageNumber * pageSize, data?.totalCount ?? 0)} of {data?.totalCount ?? 0}
  </span>
  <div className="flex gap-2">
    <Button variant="outline" size="sm" onClick={() => setPageNumber(p => p - 1)} disabled={pageNumber === 1}>
      Previous
    </Button>
    <Button
      variant="outline"
      size="sm"
      onClick={() => setPageNumber(p => p + 1)}
      disabled={!data || pageNumber * pageSize >= data.totalCount}
    >
      Next
    </Button>
  </div>
</div>
```

Update the `queryKeys.nodes` factory to include `pageNumber` and `pageSize` so different pages are cached separately:

In `queryKeys.ts`, change:
```typescript
// Before:
nodes: () => ['nodes'] as const,
// After:
nodes: (pageNumber = 1, pageSize = 50) => ['nodes', pageNumber, pageSize] as const,
```

Update all callers of `queryKeys.nodes()` to pass the page params. (Check what calls `queryKeys.nodes()` — typically the SignalR event router invalidation. Update it to `queryClient.invalidateQueries({ queryKey: ['nodes'] })` without args so it invalidates all pages.)

- [ ] **Step 13: Build the frontend — expect zero TypeScript errors**

```pwsh
cd D:\MSOSync\src\MSOSync.Frontend
npm run build 2>&1 | Select-Object -Last 15
```

Expected: `built in X.XXs` with 0 errors. Fix any type errors before proceeding.

Common errors to expect:
- `page` property doesn't exist on the new filter types — remove `page: 1` from any remaining usages
- Mismatch between `data.data` (old) and `data.items` (new) in any component that accesses events directly — search for `data?.data` and update to `data?.items` where the data comes from an events/batches/audit endpoint
- Missing `signal` param somewhere — make sure all 4 API functions accept and forward it

- [ ] **Step 14: Commit**

```pwsh
cd D:\MSOSync
git add `
  src/MSOSync.Frontend/src/shared/types/common.ts `
  src/MSOSync.Frontend/src/shared/api/events.ts `
  src/MSOSync.Frontend/src/shared/api/batches.ts `
  src/MSOSync.Frontend/src/shared/api/audit.ts `
  src/MSOSync.Frontend/src/shared/api/nodes.ts `
  src/MSOSync.Frontend/src/shared/queryKeys.ts `
  src/MSOSync.Frontend/src/shared/hooks/useInfiniteEvents.ts `
  src/MSOSync.Frontend/src/shared/hooks/useInfiniteIncomingBatches.ts `
  src/MSOSync.Frontend/src/shared/hooks/useInfiniteOutgoingBatches.ts `
  src/MSOSync.Frontend/src/shared/hooks/useInfiniteAudit.ts `
  src/MSOSync.Frontend/src/features/events/EventsPage.tsx `
  src/MSOSync.Frontend/src/features/events/EventsGrid.tsx `
  src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx `
  src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesGrid.tsx `
  src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx `
  src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesGrid.tsx `
  src/MSOSync.Frontend/src/features/audit/AuditPage.tsx `
  src/MSOSync.Frontend/src/features/audit/AuditGrid.tsx `
  src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx

git commit -m "feat(11g): cursor pagination frontend — useInfinite hooks + Load More UX + Nodes offset pagination"
```

## Status Report Format

```
Status: DONE
Commit: <sha>
Build: clean (0 TS errors)
Concerns: <none or list>
```
