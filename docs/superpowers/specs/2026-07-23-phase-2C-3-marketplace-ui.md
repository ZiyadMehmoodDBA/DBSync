# Phase 2C.3 — Marketplace UI + Auto-update: Design Specification

**Date:** 2026-07-23
**Status:** Approved
**Phase:** 2C — SDK & Ecosystem
**Prerequisite:** Phase 2C.2 (Plugin Marketplace Backend) must be complete and all `/api/v1/marketplace/*` endpoints must be deployed before this UI is built.

---

## Goal

Deliver the frontend surface for the plugin marketplace: a searchable catalog page (`/marketplace`) where administrators can discover, inspect, and install plugins directly from the UI, plus an updates panel on the existing `/administration/plugins` page that surfaces available updates and allows one-click updates. A red badge on the nav item and in the notification bell signals when updates are available. The feature degrades gracefully to an empty state when the backend marketplace is not configured (503).

---

## Architecture

### File List with Responsibilities

```
src/MSOSync.Frontend/src/
│
├── shared/
│   ├── types/
│   │   └── marketplace.ts                 ← all TypeScript interfaces matching backend DTOs exactly
│   ├── api/
│   │   └── marketplace.ts                 ← all raw API functions (client.get / client.post)
│   └── hooks/
│       └── useMarketplace.ts              ← all TanStack Query hooks + mutations for marketplace
│
├── features/
│   └── plugins/
│       ├── MarketplacePage.tsx            ← /marketplace route: search bar, category filter, sort,
│       │                                     plugin grid, plugin detail drawer, install flow
│       ├── MarketplacePage.test.tsx       ← Vitest + RTL component tests for MarketplacePage
│       ├── MarketplacePluginCard.tsx      ← single plugin card: icon, name, author, rating, install btn
│       ├── MarketplacePluginCard.test.tsx ← component tests for PluginCard
│       ├── MarketplacePluginDrawer.tsx    ← shadcn Sheet: full description, version list,
│       │                                     changelog, "Install version" dropdown
│       ├── MarketplaceStarRating.tsx      ← read-only star rating display (lucide Star icons)
│       ├── UpdatesPanel.tsx               ← tab/section added to PluginsPage: check for updates,
│       │                                     update list, per-plugin Update button, Update All
│       └── UpdatesPanel.test.tsx          ← component tests for UpdatesPanel
│
└── app/
    ├── router.tsx                         ← add /marketplace route (eager import, PermissionGuard)
    └── layouts/
        └── AppLayout.tsx                  ← add "Marketplace" nav item to Administration group
                                              with update count badge; extend permMap
```

**Dependency rule:** `shared/api/marketplace.ts` imports only `client` and types from `shared/types/marketplace.ts`. Hooks in `shared/hooks/useMarketplace.ts` import the API functions and `queryKeys`. Page components import hooks, not API functions directly. This mirrors the pattern established by `features/plugins/{api,hooks,PluginsPage}`.

---

## API Types — TypeScript Interfaces

**File:** `src/MSOSync.Frontend/src/shared/types/marketplace.ts`

These interfaces match the backend DTOs from Phase 2C.2 exactly. Property names match the JSON shapes produced by `System.Text.Json` with `JsonSerializerDefaults.Web` (camelCase).

```typescript
// ── Search / List ──────────────────────────────────────────────────────────────

export interface MarketplacePluginListItemDto {
  id:             string;
  name:           string;
  author:         string;
  description:    string;
  category:       string;
  tags:           string[];
  latestVersion:  string;
  minHostVersion: string;
  downloadCount:  number;
  rating:         number;      // 0.0–5.0
  ratingCount:    number;
  publishedAt:    string;      // ISO-8601
  updatedAt:      string;      // ISO-8601
  iconUrl:        string | null;
  verified:       boolean;
}

// ── Detail ────────────────────────────────────────────────────────────────────

export interface MarketplaceVersionDto {
  version:        string;
  minHostVersion: string;
  maxHostVersion: string;
  publishedAt:    string;      // ISO-8601
  downloadUrl:    string;
  sha256:         string;
  releaseNotes:   string | null;
  deprecated:     boolean;
}

export interface MarketplacePluginDetailDto {
  id:             string;
  name:           string;
  author:         string;
  description:    string;
  category:       string;
  tags:           string[];
  latestVersion:  string;
  minHostVersion: string;
  downloadCount:  number;
  rating:         number;
  ratingCount:    number;
  publishedAt:    string;
  updatedAt:      string;
  iconUrl:        string | null;
  projectUrl:     string | null;
  licenseId:      string | null;
  verified:       boolean;
  versions:       MarketplaceVersionDto[];
}

// ── Paged search response envelope ───────────────────────────────────────────

export interface MarketplaceSearchResult {
  data:       MarketplacePluginListItemDto[];
  total:      number;
  page:       number;
  pageSize:   number;
  totalPages: number;
}

// ── Install ───────────────────────────────────────────────────────────────────

export interface MarketplaceInstallRequest {
  version?: string;   // omit to install latest
}

export interface MarketplaceInstallResult {
  success:         boolean;
  pluginId:        string;
  installedVersion: string;
  restartRequired: boolean;
  errorMessage:    string | null;
}

// ── Update check ──────────────────────────────────────────────────────────────

export interface MarketplaceUpdateManifestDto {
  pluginId:          string;
  installedVersion:  string;
  availableVersion:  string;
  downloadUrl:       string;
  sha256:            string;
  releaseNotes:      string | null;
  publishedAt:       string;   // ISO-8601
}

export interface BulkUpdateCheckRequest {
  updatesOnly: boolean;
}

export interface BulkUpdateCheckResult {
  totalChecked:     number;
  updatesAvailable: number;
  updates:          MarketplaceUpdateManifestDto[];
}

// ── Search parameters (local, not sent directly — used to build query params) ─

export interface MarketplaceSearchParams {
  query?:    string;
  category?: string;
  page:      number;
  pageSize:  number;
  sort?:     MarketplaceSortOrder;
}

export type MarketplaceSortOrder = 'newest' | 'popular' | 'rating';

// ── Categories (static list, not fetched from backend) ────────────────────────

export const MARKETPLACE_CATEGORIES = [
  'All',
  'Collector',
  'Transformer',
  'Publisher',
  'Routing',
  'Security',
  'Analytics',
  'Utility',
] as const;

export type MarketplaceCategory = typeof MARKETPLACE_CATEGORIES[number];
```

---

## API Functions

**File:** `src/MSOSync.Frontend/src/shared/api/marketplace.ts`

All functions use the shared `client` instance (axios, `baseURL: '/api/v1'`). They follow the same pattern as `shared/api/users.ts` and `shared/api/events.ts`: destructure `data` from the axios response and return typed values.

```typescript
import client from './client';
import type {
  MarketplaceSearchResult,
  MarketplacePluginDetailDto,
  MarketplaceVersionDto,
  MarketplaceInstallRequest,
  MarketplaceInstallResult,
  MarketplaceUpdateManifestDto,
  BulkUpdateCheckRequest,
  BulkUpdateCheckResult,
} from '../types/marketplace';

// ── Search ────────────────────────────────────────────────────────────────────

export async function searchMarketplace(
  query:    string | undefined,
  category: string | undefined,
  page:     number,
  pageSize: number,
  options?: { signal?: AbortSignal },
): Promise<MarketplaceSearchResult> {
  const { data } = await client.get<MarketplaceSearchResult>(
    '/marketplace/plugins',
    {
      params: {
        ...(query    ? { query }    : {}),
        ...(category && category !== 'All' ? { category } : {}),
        page,
        pageSize,
      },
      signal: options?.signal,
    },
  );
  return data;
}

// ── Detail ────────────────────────────────────────────────────────────────────

export async function getMarketplacePlugin(
  id:      string,
  options?: { signal?: AbortSignal },
): Promise<MarketplacePluginDetailDto> {
  const { data } = await client.get<MarketplacePluginDetailDto>(
    `/marketplace/plugins/${encodeURIComponent(id)}`,
    { signal: options?.signal },
  );
  return data;
}

// ── Versions ──────────────────────────────────────────────────────────────────

export async function getMarketplaceVersions(
  id:      string,
  options?: { signal?: AbortSignal },
): Promise<MarketplaceVersionDto[]> {
  const { data } = await client.get<MarketplaceVersionDto[]>(
    `/marketplace/plugins/${encodeURIComponent(id)}/versions`,
    { signal: options?.signal },
  );
  return data;
}

// ── Install ───────────────────────────────────────────────────────────────────

export async function installMarketplacePlugin(
  id:      string,
  request: MarketplaceInstallRequest,
): Promise<MarketplaceInstallResult> {
  const { data } = await client.post<MarketplaceInstallResult>(
    `/marketplace/plugins/${encodeURIComponent(id)}/install`,
    request,
  );
  return data;
}

// ── Single plugin update check ────────────────────────────────────────────────

export async function checkPluginUpdate(
  id:      string,
  options?: { signal?: AbortSignal },
): Promise<MarketplaceUpdateManifestDto | null> {
  // Backend returns 204 when no update is available.
  // Axios resolves 204 with data = '' — normalise to null.
  const { data, status } = await client.get<MarketplaceUpdateManifestDto | ''>(
    `/marketplace/plugins/${encodeURIComponent(id)}/updates`,
    { signal: options?.signal },
  );
  if (status === 204 || data === '') return null;
  return data as MarketplaceUpdateManifestDto;
}

// ── Bulk update check ─────────────────────────────────────────────────────────

export async function checkAllUpdates(
  request: BulkUpdateCheckRequest = { updatesOnly: true },
): Promise<BulkUpdateCheckResult> {
  const { data } = await client.post<BulkUpdateCheckResult>(
    '/marketplace/updates/check',
    request,
  );
  return data;
}
```

**503 handling:** All functions let axios throw on non-2xx responses. The hooks detect the 503 status code and return a `isMarketplaceUnconfigured` flag instead of treating it as a generic error. See hook signatures below.

---

## Query Keys

The following keys are added to `src/MSOSync.Frontend/src/shared/queryKeys.ts`. They follow the existing `plugins: { all, detail, summary }` pattern:

```typescript
marketplace: {
  search: (params: { query?: string; category?: string; page: number; pageSize: number }) =>
    ['marketplace', 'search', params] as const,
  detail: (id: string) =>
    ['marketplace', 'plugin', id] as const,
  versions: (id: string) =>
    ['marketplace', 'versions', id] as const,
  updates: () =>
    ['marketplace', 'updates'] as const,
  updateCount: () =>
    ['marketplace', 'update-count'] as const,
},
```

---

## Hook Signatures

**File:** `src/MSOSync.Frontend/src/shared/hooks/useMarketplace.ts`

All hooks follow the pattern in `features/plugins/hooks.ts`: `useQuery` / `useMutation` from TanStack Query v5, `toast` from `sonner`, `useQueryClient` for cache invalidation.

### Helpers

```typescript
// Returns true when an axios error has HTTP status 503.
function isUnconfiguredError(error: unknown): boolean;
```

### `useMarketplaceSearch`

```typescript
export function useMarketplaceSearch(params: {
  query?:    string;
  category?: string;
  page:      number;
  pageSize:  number;
}): {
  data:                    MarketplaceSearchResult | undefined;
  isLoading:               boolean;
  isError:                 boolean;
  isMarketplaceUnconfigured: boolean;
  error:                   unknown;
};
```

- `queryKey`: `queryKeys.marketplace.search(params)`
- `queryFn`: calls `searchMarketplace`
- `staleTime`: `60_000` (1 minute — catalog rarely changes mid-session)
- Sets `isMarketplaceUnconfigured = true` when the error has HTTP status 503

### `useMarketplacePlugin`

```typescript
export function useMarketplacePlugin(id: string | null): {
  data:                    MarketplacePluginDetailDto | undefined;
  isLoading:               boolean;
  isError:                 boolean;
  isMarketplaceUnconfigured: boolean;
  error:                   unknown;
};
```

- `enabled`: `id !== null`
- `queryKey`: `queryKeys.marketplace.detail(id!)`
- `staleTime`: `120_000`

### `useInstallPlugin`

```typescript
export function useInstallPlugin(): UseMutationResult<
  MarketplaceInstallResult,
  unknown,
  { id: string; version?: string; name: string }
>;
```

- `mutationFn`: `({ id, version }) => installMarketplacePlugin(id, { version })`
- `onSuccess`: if `result.success` → `toast.success(\`"${name}" v${result.installedVersion} installed. Restart required.\`)` and invalidate `queryKeys.plugins.all()`; if `!result.success` → `toast.error(result.errorMessage ?? 'Install failed.')`
- `onError`: `toast.error('Install failed. Check server logs.')`

### `useCheckAllUpdates`

```typescript
export function useCheckAllUpdates(): {
  data:                    BulkUpdateCheckResult | undefined;
  isLoading:               boolean;
  isMarketplaceUnconfigured: boolean;
  refetch:                 () => void;
};
```

- `queryKey`: `queryKeys.marketplace.updates()`
- `queryFn`: `() => checkAllUpdates({ updatesOnly: true })`
- `staleTime`: `300_000` (5 minutes — update checks are relatively expensive)
- `refetchOnWindowFocus`: `false`
- Sets `isMarketplaceUnconfigured = true` on 503; does not set `isError` in that case so the panel renders the unconfigured empty state rather than an error

### `useUpdateCount`

```typescript
export function useUpdateCount(): number;
```

- Reads from `queryKeys.marketplace.updateCount()` in the query cache
- `queryFn`: `() => checkAllUpdates({ updatesOnly: true }).then(r => r.updatesAvailable)`
- `staleTime`: `300_000`
- `refetchOnWindowFocus`: `false`
- Returns `0` when data is undefined (e.g., marketplace unconfigured or not yet loaded)
- This hook is called in `AppLayout` for the nav badge; it must be lightweight and non-throwing

### `useUpdatePlugin`

```typescript
export function useUpdatePlugin(): UseMutationResult<
  MarketplaceInstallResult,
  unknown,
  { id: string; version: string; name: string }
>;
```

- `mutationFn`: `({ id, version }) => installMarketplacePlugin(id, { version })`
- Identical to `useInstallPlugin` success/error handling
- `onSuccess`: additionally invalidates `queryKeys.marketplace.updates()` and `queryKeys.marketplace.updateCount()` so badge refreshes

### `useMarketplaceUnconfigured`

```typescript
export function useMarketplaceUnconfigured(): boolean;
```

- Derives from `useCheckAllUpdates().isMarketplaceUnconfigured`
- Used by `UpdatesPanel` to show the unconfigured empty state without polling

---

## Component Hierarchy

```
/marketplace  (MarketplacePage)
├── <PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>
│   ├── Page header ("Marketplace", subtitle)
│   ├── Toolbar row
│   │   ├── Search input (controlled, debounced 300ms)
│   │   ├── Category <Select> (shadcn Select, MARKETPLACE_CATEGORIES)
│   │   └── Sort <Select> (Newest / Most Downloaded / Top Rated)
│   ├── Loading state: 12-card skeleton grid
│   ├── Unconfigured state: <EmptyState message="Marketplace not configured. ...">
│   ├── Error state: <ErrorState error={error} onRetry={refetch}>
│   ├── Empty results: <EmptyState message="No plugins found.">
│   ├── Plugin grid (CSS grid, 3 columns on lg, 2 on md, 1 on sm)
│   │   └── <MarketplacePluginCard> × N
│   ├── Pagination row (Previous / page X of Y / Next — shadcn Button)
│   └── <MarketplacePluginDrawer> (shadcn Sheet, open when selectedPluginId != null)
│       ├── Plugin header (icon, name, author, verified badge, category tag)
│       ├── Rating + download count row
│       ├── Full description
│       ├── Tags
│       ├── Project URL / License links (when present)
│       ├── Version selector + "Install" button
│       │   ├── <Select> of all non-deprecated versions (default: latest)
│       │   └── <Button> → useInstallPlugin.mutate(...)
│       └── Changelog section (release notes for selected version)

/administration/plugins  (PluginsPage — modified, not replaced)
├── Existing plugin table (unchanged)
└── Updates section (new, below existing table)
    └── <UpdatesPanel>
        ├── Section header "Plugin Updates"
        ├── "Check for Updates" <Button> → calls refetch()
        ├── Loading spinner (while isLoading)
        ├── Unconfigured: <EmptyState message="Marketplace not configured.">
        ├── No updates: <EmptyState message="All plugins are up to date.">
        ├── Updates list
        │   ├── "Update All" <Button> (disabled while any update is pending)
        │   └── Per-plugin row:
        │       ├── Plugin name + pluginId
        │       ├── Current version → available version (arrow)
        │       ├── Release notes snippet (first 100 chars, truncated)
        │       └── "Update" <Button> → useUpdatePlugin.mutate(...)
        │           with per-plugin loading spinner while that mutation is pending
        └── Per-plugin loading state tracked via Set<string> (pluginIds in-flight)
```

---

## `MarketplacePluginCard` Props and Layout

**File:** `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginCard.tsx`

```typescript
interface MarketplacePluginCardProps {
  plugin:      MarketplacePluginListItemDto;
  isInstalled: boolean;                     // true when pluginId is in the installed plugins list
  onSelect:    (id: string) => void;        // opens detail drawer
  onInstall:   (id: string, name: string) => void;
  isInstalling: boolean;                    // true while install mutation is pending for this id
}
```

**Card layout (left to right, top to bottom):**

1. Icon: `iconUrl` rendered as `<img>` with a `Package` lucide fallback (32×32, rounded)
2. Verified badge: `ShieldCheck` icon from lucide in blue, tooltip "Verified publisher", shown only when `plugin.verified`
3. Name (`font-medium`) + Author (`text-xs text-neutral-500`)
4. Category badge: `<span className="rounded-full px-2 py-0.5 text-xs bg-neutral-100 dark:bg-neutral-800">`
5. Description: 2-line clamp (`line-clamp-2 text-sm text-neutral-600`)
6. Footer row: `<MarketplaceStarRating rating={plugin.rating} />` + download count formatted with `Intl.NumberFormat` (e.g. "12.4K"), version tag
7. Action: "Installed" `<Badge>` (green, shadcn Badge variant `secondary`) when `isInstalled`; "Install" `<Button size="sm">` when not installed (shows `<Loader2 className="animate-spin" />` when `isInstalling`)

Clicking anywhere on the card body (except the Install button) calls `onSelect(plugin.id)`.

---

## `MarketplaceStarRating` Props

**File:** `src/MSOSync.Frontend/src/features/plugins/MarketplaceStarRating.tsx`

```typescript
interface MarketplaceStarRatingProps {
  rating:      number;   // 0.0–5.0
  ratingCount: number;
  showCount?:  boolean;  // default true
}
```

Renders 5 `Star` icons from lucide-react. Full star when `index + 1 <= Math.floor(rating)`, partial fill approximated with opacity for the fractional star, empty for the rest. `ratingCount` shown in parentheses when `showCount` is true.

---

## `MarketplacePluginDrawer` Props

**File:** `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginDrawer.tsx`

```typescript
interface MarketplacePluginDrawerProps {
  pluginId:    string | null;     // null = closed
  onClose:     () => void;
  isInstalled: boolean;
  onInstall:   (id: string, version: string, name: string) => void;
  isInstalling: boolean;
}
```

Uses shadcn `Sheet` (`side="right"`, `className="w-[480px] sm:w-[540px]"`). Fetches detail via `useMarketplacePlugin(pluginId)`. Version selector is a shadcn `Select`; default value is `detail.latestVersion`. Non-deprecated versions only appear in the selector; deprecated versions appear dimmed with a "(deprecated)" suffix. Install button calls `onInstall(pluginId, selectedVersion, detail.name)`. Release notes rendered in a `<pre className="whitespace-pre-wrap text-xs font-mono">` block.

---

## `UpdatesPanel` Props

**File:** `src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.tsx`

```typescript
interface UpdatesPanelProps {
  installedPluginIds: string[];   // from usePlugins() in PluginsPage — passed as prop, not re-fetched
}
```

Internally calls `useCheckAllUpdates()` and `useUpdatePlugin()`. The "Update All" button iterates `data.updates` and calls `useUpdatePlugin.mutate` for each in sequence (not in parallel) using a `for...of` loop with `await mutateAsync(...)` inside an async handler. Progress is tracked via a local `Set<string>` state: each plugin ID is added when its update begins and removed on completion.

---

## Auto-update Notification Flow

### On App Load

The `useUpdateCount` hook is called once inside `AppLayout` (alongside `usePreferences()` and `usePermissions()`). It fires a background query when the component mounts. Because `staleTime` is 5 minutes, it does not re-fire on every render.

```typescript
// Inside AppLayout, alongside existing usePreferences() / usePermissions() calls:
const updateCount = useUpdateCount();
```

`updateCount` is passed into the `NAV_GROUPS` configuration (or read directly in `NavGroup`) to append a badge on the "Marketplace" nav item.

### Nav Badge

The "Marketplace" nav item renders a red count badge when `updateCount > 0`:

```tsx
<NavLink to="/marketplace" ...>
  <Store className="h-4 w-4 shrink-0" />
  Marketplace
  {updateCount > 0 && (
    <span className="ml-auto flex h-4 w-4 items-center justify-center rounded-full bg-red-500 text-[10px] font-bold text-white">
      {updateCount > 99 ? '99+' : updateCount}
    </span>
  )}
</NavLink>
```

The `NavItem` type in `AppLayout.tsx` is extended to support an optional `badgeCount?: number` field. Alternatively, the Marketplace nav item can be rendered as a special case inside the Administration `NavGroup`.

### Notification Bell Integration

When `updateCount > 0`, a synthetic "plugin updates available" notification is injected at the top of the `NotificationBell` popover. This is a client-side-only notification (not persisted to the server notification API). It is rendered as a static list item above the divider that leads to the fetched server notifications.

```tsx
// Inside NotificationBell, before the items.map(...):
{updateCount > 0 && (
  <div className="flex items-center gap-3 px-4 py-3 bg-blue-50 dark:bg-blue-900/20">
    <Store className="h-4 w-4 text-blue-600 dark:text-blue-400 shrink-0" />
    <div className="flex-1 min-w-0">
      <p className="text-sm font-medium text-blue-900 dark:text-blue-100">
        {updateCount} plugin {updateCount === 1 ? 'update' : 'updates'} available
      </p>
      <Link
        to="/marketplace"
        onClick={() => setOpen(false)}
        className="text-xs text-blue-600 hover:underline dark:text-blue-400"
      >
        View updates →
      </Link>
    </div>
  </div>
)}
```

`NotificationBell` must receive `updateCount` as a prop or call `useUpdateCount()` directly. Given `useUpdateCount` is a lightweight cache read (no new network call if `AppLayout` already fetched it), calling it directly in `NotificationBell` is preferred — it shares the same TanStack Query cache entry.

### 503 Handling in Auto-update

`useUpdateCount` must not throw or show an error when the marketplace is unconfigured. When the backend returns 503, the hook returns `0` silently. This is implemented by wrapping the `queryFn` in a try/catch that checks for HTTP 503 and returns `{ updatesAvailable: 0, ... }` instead of re-throwing.

---

## Router Changes

**File:** `src/MSOSync.Frontend/src/app/router.tsx`

Add one new route inside the `AuthGuard → AppLayout` children array, matching the existing eager-import pattern (no `lazy()`):

```typescript
import { MarketplacePage } from '../features/plugins/MarketplacePage';

// Inside the children array, alongside other administration routes:
{
  path: 'marketplace',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>
      <MarketplacePage />
    </PermissionGuard>
  ),
},
```

The `PermissionGuard` pattern is identical to the `administration/plugins` route.

---

## Nav Changes

**File:** `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`

1. Import `Store` from `lucide-react`.
2. Import `useUpdateCount` from `../../shared/hooks/useMarketplace`.
3. Call `const updateCount = useUpdateCount()` inside `AppLayout`, alongside `usePreferences()`.
4. Add "Marketplace" to the Administration `NAV_GROUPS` entry:

```typescript
{ label: 'Marketplace', path: '/marketplace', icon: Store, requiredPermission: PermissionKeys.ManagePlugins },
```

5. Extend `NavGroup` to accept an optional `badgeCount` per item, or render the Marketplace item as a special case using `updateCount` from a prop passed down from `AppLayout`.

**Recommended approach (minimal changes):** Thread `updateCount` as a prop through `NavGroup → NavLink` only for the Marketplace item by checking `path === '/marketplace'` inside `NavGroup`. This avoids restructuring the `NavItem` type.

6. Add `canManagePlugins` to `permMap` in `NavGroup` (already present — verify it maps `PermissionKeys.ManagePlugins`).

---

## PluginsPage Changes

**File:** `src/MSOSync.Frontend/src/features/plugins/PluginsPage.tsx`

Add `<UpdatesPanel installedPluginIds={plugins?.map(p => p.pluginId) ?? []} />` below `<PluginSummaryCard />`, wrapped in a `<div className="space-y-6">`. No other changes to the existing plugin table.

```tsx
// Minimal addition after PluginSummaryCard:
<UpdatesPanel installedPluginIds={plugins?.map(p => p.pluginId) ?? []} />
```

Because `plugins` may be undefined while loading, the fallback `[]` means `UpdatesPanel` renders but `installedPluginIds` is empty — this is acceptable since the updates list is independent of `installedPluginIds` (the backend checks all installed plugins server-side).

---

## Testing

### Test Files

| File | Framework | Scope |
|---|---|---|
| `MarketplacePage.test.tsx` | Vitest + RTL | Page-level rendering and user interactions |
| `MarketplacePluginCard.test.tsx` | Vitest + RTL | Card component in isolation |
| `UpdatesPanel.test.tsx` | Vitest + RTL | Updates panel states |

### Mocking Pattern

Follow `PluginsPage.test.tsx` exactly: `vi.mock` the hooks module, not the API module. Return controlled data from hook mocks. Wrap renders in a `QueryClientProvider` with `retry: false`.

```typescript
vi.mock('../../shared/hooks/useMarketplace', () => ({
  useMarketplaceSearch: () => ({ ... }),
  useInstallPlugin:     () => ({ mutate: vi.fn(), isPending: false }),
  useCheckAllUpdates:   () => ({ ... }),
  useUpdatePlugin:      () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateCount:       () => 0,
}));
```

### `MarketplacePage.test.tsx` Test Cases

| Test | Scenario |
|---|---|
| `renders search bar and category filter` | `isLoading: false`, empty data → search input and category select are present |
| `renders unconfigured empty state on 503` | `isMarketplaceUnconfigured: true` → shows "Marketplace not configured" message |
| `renders plugin grid when data available` | 3 plugins in mock data → 3 cards in DOM |
| `renders loading skeleton while fetching` | `isLoading: true` → skeleton elements present, no grid |
| `renders error state on non-503 error` | `isError: true`, `isMarketplaceUnconfigured: false` → `<ErrorState>` rendered |
| `calls onInstall when Install button clicked` | Click "Install" on a card → `useInstallPlugin.mutate` called with correct id |
| `opens drawer when plugin card body clicked` | Click card body → `MarketplacePluginDrawer` receives non-null `pluginId` |
| `pagination Previous/Next updates page param` | Click Next → search params page increments |

### `MarketplacePluginCard.test.tsx` Test Cases

| Test | Scenario |
|---|---|
| `renders plugin name and author` | Name and author text present |
| `renders Installed badge when isInstalled` | `isInstalled: true` → "Installed" badge, no "Install" button |
| `renders Install button when not installed` | `isInstalled: false` → "Install" button present |
| `renders loading spinner when isInstalling` | `isInstalling: true` → spinner icon present, button disabled |
| `renders verified badge for verified plugins` | `verified: true` → `ShieldCheck` icon present |
| `renders Package fallback icon when no iconUrl` | `iconUrl: null` → Package icon rendered |

### `UpdatesPanel.test.tsx` Test Cases

| Test | Scenario |
|---|---|
| `renders unconfigured state when marketplace not configured` | `isMarketplaceUnconfigured: true` → "Marketplace not configured" message |
| `renders no updates message when all up to date` | `data.updates: []` → "All plugins are up to date" |
| `renders update rows for available updates` | 2 updates in data → 2 rows with plugin name, version change, Update button |
| `renders loading spinner while checking` | `isLoading: true` → spinner present |
| `Update All button calls mutate for each plugin` | Click "Update All" → `useUpdatePlugin.mutate` called twice (once per update) |
| `Update button shows spinner while pending` | `isPending: true` for pluginId → spinner on that row's button |

---

## Global Constraints

| Constraint | Detail |
|---|---|
| Admin-only access | `/marketplace` route is wrapped in `<PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>`. `PermissionGuard` shows `<PermissionDeniedPage>` for unauthorized users. No page-level role check is needed inside `MarketplacePage` itself. |
| Existing patterns only | All API functions go in `shared/api/marketplace.ts`. All hooks go in `shared/hooks/useMarketplace.ts`. Types go in `shared/types/marketplace.ts`. Do not add marketplace-specific logic to any other shared file except `queryKeys.ts` (new keys) and `AppLayout.tsx` / `router.tsx` (wiring). |
| No lazy imports in router | All pages in `router.tsx` use eager imports. `MarketplacePage` must be imported eagerly. |
| Toast library | Use `sonner` (`import { toast } from 'sonner'`). Do not use any other toast mechanism. |
| `client` import path | `import client from './client'` (relative, from `shared/api/marketplace.ts`). Never import axios directly in feature code. |
| Query invalidation on install | After a successful install, invalidate `queryKeys.plugins.all()` so the installed plugins table on `/administration/plugins` refreshes on next visit. |
| Query invalidation on update | After a successful update, invalidate `queryKeys.plugins.all()`, `queryKeys.marketplace.updates()`, and `queryKeys.marketplace.updateCount()`. |
| 503 = unconfigured, not error | When any marketplace API call returns HTTP 503, the UI must show "Marketplace not configured" and must NOT show `<ErrorState>` or any red error text. |
| `useUpdateCount` non-throwing | `useUpdateCount` must return `0` silently on 503, network errors, or any other failure. It must never cause `AppLayout` to render an error boundary. |
| `staleTime` discipline | `useMarketplaceSearch`: 60 s. `useMarketplacePlugin`: 120 s. `useCheckAllUpdates` / `useUpdateCount`: 300 s. Do not use `staleTime: 0` or `staleTime: Infinity` for marketplace queries. |
| No `Task.WhenAll` equivalent in Update All | "Update All" in `UpdatesPanel` must call `mutateAsync` sequentially in a `for...of` loop, not via `Promise.all`. This mirrors the backend constraint and avoids hammering the installer. |
| Debounced search | The search input is debounced 300 ms before updating the `query` state that feeds `useMarketplaceSearch`. Use a local `useEffect` + `setTimeout` / `clearTimeout` pattern or a utility hook. Do not fire a new query on every keystroke. |
| Pagination page size | Default `pageSize: 20`. The user cannot change page size in v1 of this feature. |
| Sort | Sort order is a UI-side state (`MarketplaceSortOrder`). Because the backend `GET /marketplace/plugins` does not accept a `sort` parameter in Phase 2C.2, the frontend sorts the returned `data` array client-side: `newest` by `updatedAt` desc, `popular` by `downloadCount` desc, `rating` by `rating` desc. Do not add a `sort` query param to the API call. |
| `isInstalled` resolution | `MarketplacePluginCard` receives `isInstalled` as a prop. `MarketplacePage` computes it by checking whether `plugin.id` exists in the set of installed plugin IDs obtained from `usePlugins()` (already available in the shared query cache). No additional API call needed. |
| Icon image errors | `<img src={plugin.iconUrl}>` must have an `onError` handler that hides the image and shows the `Package` fallback icon. |
| Accessibility | Search input has `aria-label="Search plugins"`. Category select has `aria-label="Filter by category"`. Star rating has `aria-label={\`Rated ${rating} out of 5\`}`. Install buttons have `aria-label={\`Install ${plugin.name}\`}`. Update buttons have `aria-label={\`Update ${manifest.pluginId} to ${manifest.availableVersion}\`}`. |
| Component file placement | All marketplace components live in `src/features/plugins/`. They do not live in `src/shared/components/`. Only the API, hooks, and types are in `src/shared/`. |
| No new shared component files | Do not create new files under `src/shared/components/`. Reuse existing `EmptyState`, `ErrorState`, `Button`, `Sheet`, `Select`, `Badge`, `Separator` from shadcn. |

---

## Out of Scope (Deferred)

| Feature | Phase |
|---|---|
| Rating submission (star click to rate) | 2C.6 |
| Plugin detail page (full-page route, not drawer) | 2C.6 |
| Marketplace category fetch from backend (dynamic) | 2C.6 |
| Auto-update background silent install | 2C.7 |
| Sort parameter sent to backend | 2C.6 (requires backend sort support) |
| Infinite scroll / virtual list for large catalogs | 2C.7 |
| Plugin uninstall from marketplace UI | 2C.6 |
| Publisher verification badge details page | 2C.6 |
| Offline / air-gapped registry support | Enterprise |
