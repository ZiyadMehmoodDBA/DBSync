# Task 4: MarketplacePluginDrawer + MarketplacePage + Router + Nav Wiring

> Part of the [Phase 2C.3 master plan](./2026-07-23-phase-2C-3-master.md)

**Prerequisite:** Tasks 1, 2, 3 complete — types, hooks, and card components must exist.

**Goal:** Build `MarketplacePluginDrawer`, the full `MarketplacePage` (search, filter, sort, grid, pagination), wire the route in `router.tsx`, add the "Marketplace" nav item with update count badge to `AppLayout`, and write all `MarketplacePage` tests.

**Files:**
- Create: `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginDrawer.tsx`
- Create: `src/MSOSync.Frontend/src/features/plugins/MarketplacePage.tsx`
- Create: `src/MSOSync.Frontend/src/features/plugins/MarketplacePage.test.tsx`
- Modify: `src/MSOSync.Frontend/src/app/router.tsx`
- Modify: `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`

**Interfaces:**
- Consumes: `useMarketplaceSearch`, `useMarketplacePlugin`, `useInstallPlugin`, `useUpdateCount` from `shared/hooks/useMarketplace`
- Consumes: `usePlugins` from `features/plugins/hooks`
- Consumes: `MarketplacePluginCard`, `MarketplaceStarRating` from `features/plugins/`
- Produces: `MarketplacePage` (exported, registered at route `marketplace`)

---

- [ ] **Step 1: Create `MarketplacePluginDrawer.tsx`**

Create `src/MSOSync.Frontend/src/features/plugins/MarketplacePluginDrawer.tsx`:

```typescript
import { useState, useEffect } from 'react';
import { ShieldCheck, ExternalLink } from 'lucide-react';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from '../../components/ui/sheet';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { Button } from '../../components/ui/button';
import { Badge } from '../../components/ui/badge';
import { Separator } from '../../components/ui/separator';
import { MarketplaceStarRating } from './MarketplaceStarRating';
import { useMarketplacePlugin } from '../../shared/hooks/useMarketplace';

interface MarketplacePluginDrawerProps {
  pluginId:     string | null;   // null = closed
  onClose:      () => void;
  isInstalled:  boolean;
  onInstall:    (id: string, version: string, name: string) => void;
  isInstalling: boolean;
}

export function MarketplacePluginDrawer({
  pluginId,
  onClose,
  isInstalled,
  onInstall,
  isInstalling,
}: MarketplacePluginDrawerProps) {
  const { data: detail, isLoading } = useMarketplacePlugin(pluginId);
  const [selectedVersion, setSelectedVersion] = useState<string>('');

  // Reset selected version when a new plugin is opened
  useEffect(() => {
    if (detail) {
      setSelectedVersion(detail.latestVersion);
    }
  }, [detail?.id, detail?.latestVersion]);

  const nonDeprecatedVersions = detail?.versions.filter(v => !v.deprecated) ?? [];
  const deprecatedVersions    = detail?.versions.filter(v => v.deprecated)  ?? [];

  const selectedVersionDetail = detail?.versions.find(v => v.version === selectedVersion);

  return (
    <Sheet open={pluginId !== null} onOpenChange={(open) => { if (!open) onClose(); }}>
      <SheetContent side="right" className="w-[480px] sm:w-[540px] overflow-y-auto">
        {isLoading && (
          <div className="flex items-center justify-center h-32 text-sm text-neutral-500">
            Loading…
          </div>
        )}

        {detail && (
          <>
            <SheetHeader className="pb-4">
              <div className="flex items-start gap-3">
                <div>
                  <SheetTitle className="flex items-center gap-2 text-left">
                    {detail.name}
                    {detail.verified && (
                      <ShieldCheck className="h-4 w-4 text-blue-500 shrink-0" aria-label="Verified publisher" />
                    )}
                  </SheetTitle>
                  <p className="text-sm text-neutral-500 mt-0.5">{detail.author}</p>
                </div>
                <Badge variant="secondary" className="ml-auto shrink-0">
                  {detail.category}
                </Badge>
              </div>

              <div className="flex items-center gap-4 pt-2">
                <MarketplaceStarRating rating={detail.rating} ratingCount={detail.ratingCount} />
                <span className="text-xs text-neutral-500">
                  {new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(detail.downloadCount)} downloads
                </span>
              </div>
            </SheetHeader>

            <Separator />

            <div className="space-y-4 py-4">
              {/* Description */}
              <div>
                <h3 className="text-sm font-medium mb-2">Description</h3>
                <p className="text-sm text-neutral-600 dark:text-neutral-400 whitespace-pre-wrap">
                  {detail.description}
                </p>
              </div>

              {/* Tags */}
              {detail.tags.length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {detail.tags.map(tag => (
                    <span
                      key={tag}
                      className="rounded-full px-2 py-0.5 text-xs bg-neutral-100 dark:bg-neutral-800 text-neutral-600 dark:text-neutral-300"
                    >
                      {tag}
                    </span>
                  ))}
                </div>
              )}

              {/* Links */}
              {(detail.projectUrl || detail.licenseId) && (
                <div className="flex items-center gap-4 text-xs">
                  {detail.projectUrl && (
                    <a
                      href={detail.projectUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="flex items-center gap-1 text-blue-600 hover:underline dark:text-blue-400"
                    >
                      <ExternalLink className="h-3 w-3" />
                      Project page
                    </a>
                  )}
                  {detail.licenseId && (
                    <span className="text-neutral-500">License: {detail.licenseId}</span>
                  )}
                </div>
              )}

              <Separator />

              {/* Version selector + install */}
              <div>
                <h3 className="text-sm font-medium mb-3">Install version</h3>
                <div className="flex items-center gap-3">
                  <Select
                    value={selectedVersion}
                    onValueChange={setSelectedVersion}
                  >
                    <SelectTrigger className="flex-1">
                      <SelectValue placeholder="Select version" />
                    </SelectTrigger>
                    <SelectContent>
                      {nonDeprecatedVersions.map(v => (
                        <SelectItem key={v.version} value={v.version}>
                          v{v.version}
                        </SelectItem>
                      ))}
                      {deprecatedVersions.map(v => (
                        <SelectItem
                          key={v.version}
                          value={v.version}
                          className="text-neutral-400"
                        >
                          v{v.version} (deprecated)
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>

                  {isInstalled ? (
                    <Badge variant="secondary" className="bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400 whitespace-nowrap">
                      Installed
                    </Badge>
                  ) : (
                    <Button
                      onClick={() => onInstall(detail.id, selectedVersion, detail.name)}
                      disabled={isInstalling || !selectedVersion}
                      aria-label={`Install ${detail.name}`}
                    >
                      {isInstalling ? 'Installing…' : 'Install'}
                    </Button>
                  )}
                </div>
              </div>

              {/* Release notes */}
              {selectedVersionDetail?.releaseNotes && (
                <div>
                  <h3 className="text-sm font-medium mb-2">
                    Release notes — v{selectedVersionDetail.version}
                  </h3>
                  <pre className="whitespace-pre-wrap text-xs font-mono bg-neutral-50 dark:bg-neutral-800/50 rounded-md p-3 text-neutral-700 dark:text-neutral-300">
                    {selectedVersionDetail.releaseNotes}
                  </pre>
                </div>
              )}
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
```

- [ ] **Step 2: Create `MarketplacePage.tsx`**

Create `src/MSOSync.Frontend/src/features/plugins/MarketplacePage.tsx`:

```typescript
import { useState, useEffect, useCallback } from 'react';
import { Search } from 'lucide-react';
import { Button } from '../../components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { EmptyState } from '../../shared/components/feedback/EmptyState';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { useMarketplaceSearch, useInstallPlugin } from '../../shared/hooks/useMarketplace';
import { usePlugins } from './hooks';
import { MarketplacePluginCard } from './MarketplacePluginCard';
import { MarketplacePluginDrawer } from './MarketplacePluginDrawer';
import {
  MARKETPLACE_CATEGORIES,
  type MarketplaceSortOrder,
  type MarketplacePluginListItemDto,
} from '../../shared/types/marketplace';

const PAGE_SIZE = 20;

function sortPlugins(
  plugins: MarketplacePluginListItemDto[],
  sort: MarketplaceSortOrder,
): MarketplacePluginListItemDto[] {
  const copy = [...plugins];
  if (sort === 'newest') {
    return copy.sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  }
  if (sort === 'popular') {
    return copy.sort((a, b) => b.downloadCount - a.downloadCount);
  }
  // 'rating'
  return copy.sort((a, b) => b.rating - a.rating);
}

export function MarketplacePage() {
  const [rawQuery,  setRawQuery]  = useState('');
  const [query,     setQuery]     = useState('');      // debounced
  const [category,  setCategory]  = useState('All');
  const [sort,      setSort]      = useState<MarketplaceSortOrder>('newest');
  const [page,      setPage]      = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Debounce search input 300 ms
  useEffect(() => {
    const timer = setTimeout(() => {
      setQuery(rawQuery);
      setPage(1);   // reset to page 1 on new search
    }, 300);
    return () => clearTimeout(timer);
  }, [rawQuery]);

  // Reset page on category change
  const handleCategoryChange = useCallback((value: string) => {
    setCategory(value);
    setPage(1);
  }, []);

  const { data, isLoading, isError, isMarketplaceUnconfigured, error, refetch } =
    useMarketplaceSearch({ query, category, page, pageSize: PAGE_SIZE });

  const installMutation = useInstallPlugin();
  const { data: installedPlugins } = usePlugins();

  const installedIdSet = new Set(installedPlugins?.map(p => p.pluginId) ?? []);

  const sortedPlugins = data ? sortPlugins(data.data, sort) : [];

  const handleInstall = useCallback(
    (id: string, name: string) => {
      installMutation.mutate({ id, name });
    },
    [installMutation],
  );

  const handleDrawerInstall = useCallback(
    (id: string, version: string, name: string) => {
      installMutation.mutate({ id, version, name });
    },
    [installMutation],
  );

  return (
    <div className="p-6 space-y-6">
      {/* Page header */}
      <div>
        <h1 className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">Marketplace</h1>
        <p className="text-sm text-neutral-500 mt-1">
          Discover and install plugins from the MSOSync plugin registry.
        </p>
      </div>

      {/* Toolbar */}
      <div className="flex items-center gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-neutral-400" />
          <input
            type="text"
            value={rawQuery}
            onChange={(e) => setRawQuery(e.target.value)}
            placeholder="Search plugins…"
            aria-label="Search plugins"
            className="w-full rounded-md border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 pl-9 pr-3 py-2 text-sm placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-300 dark:focus:ring-neutral-600"
          />
        </div>

        <Select value={category} onValueChange={handleCategoryChange}>
          <SelectTrigger className="w-44" aria-label="Filter by category">
            <SelectValue placeholder="Category" />
          </SelectTrigger>
          <SelectContent>
            {MARKETPLACE_CATEGORIES.map(c => (
              <SelectItem key={c} value={c}>{c}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select value={sort} onValueChange={(v) => setSort(v as MarketplaceSortOrder)}>
          <SelectTrigger className="w-44">
            <SelectValue placeholder="Sort by" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="newest">Newest</SelectItem>
            <SelectItem value="popular">Most Downloaded</SelectItem>
            <SelectItem value="rating">Top Rated</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* States */}
      {isLoading && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4" aria-busy="true">
          {Array.from({ length: 12 }, (_, i) => (
            <div
              key={i}
              className="rounded-lg border border-neutral-200 dark:border-neutral-700 bg-neutral-50 dark:bg-neutral-800 h-44 animate-pulse"
            />
          ))}
        </div>
      )}

      {!isLoading && isMarketplaceUnconfigured && (
        <EmptyState message="Marketplace not configured. Contact your administrator to set up the plugin registry." />
      )}

      {!isLoading && isError && !isMarketplaceUnconfigured && (
        <ErrorState error={error} onRetry={refetch} />
      )}

      {!isLoading && !isMarketplaceUnconfigured && !isError && data && (
        <>
          {sortedPlugins.length === 0 ? (
            <EmptyState message="No plugins found. Try adjusting your search or filters." />
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {sortedPlugins.map(plugin => (
                <MarketplacePluginCard
                  key={plugin.id}
                  plugin={plugin}
                  isInstalled={installedIdSet.has(plugin.id)}
                  onSelect={setSelectedId}
                  onInstall={handleInstall}
                  isInstalling={
                    installMutation.isPending &&
                    installMutation.variables?.id === plugin.id
                  }
                />
              ))}
            </div>
          )}

          {/* Pagination */}
          {data.totalPages > 1 && (
            <div className="flex items-center justify-center gap-3 pt-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
              >
                Previous
              </Button>
              <span className="text-sm text-neutral-600 dark:text-neutral-400">
                Page {data.page} of {data.totalPages}
              </span>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setPage(p => Math.min(data.totalPages, p + 1))}
                disabled={page === data.totalPages}
              >
                Next
              </Button>
            </div>
          )}
        </>
      )}

      {/* Detail drawer */}
      <MarketplacePluginDrawer
        pluginId={selectedId}
        onClose={() => setSelectedId(null)}
        isInstalled={selectedId !== null && installedIdSet.has(selectedId)}
        onInstall={handleDrawerInstall}
        isInstalling={
          installMutation.isPending &&
          installMutation.variables?.id === selectedId
        }
      />
    </div>
  );
}
```

- [ ] **Step 3: Write the failing `MarketplacePage` tests**

Create `src/MSOSync.Frontend/src/features/plugins/MarketplacePage.test.tsx`:

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MarketplacePage } from './MarketplacePage';
import type { MarketplaceSearchResult } from '../../shared/types/marketplace';

// ── Mock hooks ────────────────────────────────────────────────────────────────

const mockUseMarketplaceSearch = vi.fn();
const mockInstallMutate        = vi.fn();

vi.mock('../../shared/hooks/useMarketplace', () => ({
  useMarketplaceSearch: (...args: unknown[]) => mockUseMarketplaceSearch(...args),
  useInstallPlugin: () => ({
    mutate:     mockInstallMutate,
    isPending:  false,
    variables:  undefined,
  }),
  useUpdateCount: () => 0,
}));

vi.mock('./hooks', () => ({
  usePlugins: () => ({ data: [], isLoading: false }),
}));

// Drawer uses useMarketplacePlugin — stub to avoid a separate hook mock
vi.mock('./MarketplacePluginDrawer', () => ({
  MarketplacePluginDrawer: ({ pluginId }: { pluginId: string | null }) =>
    pluginId ? <div data-testid="drawer" data-plugin-id={pluginId} /> : null,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeSearchResult(overrides: Partial<MarketplaceSearchResult> = {}): MarketplaceSearchResult {
  return {
    data:       [],
    total:      0,
    page:       1,
    pageSize:   20,
    totalPages: 1,
    ...overrides,
  };
}

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('MarketplacePage', () => {
  beforeEach(() => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult(),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
  });

  it('renders search bar and category filter', () => {
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByRole('textbox', { name: /search plugins/i })).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: /filter by category/i })).toBeInTheDocument();
  });

  it('renders unconfigured empty state on 503', () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      undefined,
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: true,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByText(/marketplace not configured/i)).toBeInTheDocument();
  });

  it('renders plugin grid when data available', () => {
    const plugins = [
      { id: 'p1', name: 'Plugin Alpha', author: 'A', description: 'desc', category: 'Collector', tags: [], latestVersion: '1.0.0', minHostVersion: '9.0', downloadCount: 100, rating: 4.0, ratingCount: 10, publishedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z', iconUrl: null, verified: false },
      { id: 'p2', name: 'Plugin Beta',  author: 'B', description: 'desc', category: 'Utility',   tags: [], latestVersion: '2.0.0', minHostVersion: '9.0', downloadCount: 200, rating: 4.5, ratingCount: 20, publishedAt: '2026-02-01T00:00:00Z', updatedAt: '2026-07-01T00:00:00Z', iconUrl: null, verified: false },
      { id: 'p3', name: 'Plugin Gamma', author: 'C', description: 'desc', category: 'Security',  tags: [], latestVersion: '3.0.0', minHostVersion: '9.0', downloadCount: 300, rating: 5.0, ratingCount: 30, publishedAt: '2026-03-01T00:00:00Z', updatedAt: '2026-07-15T00:00:00Z', iconUrl: null, verified: true },
    ];
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ data: plugins, total: 3 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByText('Plugin Alpha')).toBeInTheDocument();
    expect(screen.getByText('Plugin Beta')).toBeInTheDocument();
    expect(screen.getByText('Plugin Gamma')).toBeInTheDocument();
  });

  it('renders loading skeleton while fetching', () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      undefined,
      isLoading:                 true,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    // 12 skeleton cards with animate-pulse
    const skeletons = document.querySelectorAll('.animate-pulse');
    expect(skeletons.length).toBe(12);
  });

  it('renders error state on non-503 error', () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      undefined,
      isLoading:                 false,
      isError:                   true,
      isMarketplaceUnconfigured: false,
      error:                     new Error('Network error'),
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    expect(screen.getByText(/network error/i)).toBeInTheDocument();
  });

  it('calls useInstallPlugin.mutate when Install button clicked', async () => {
    const plugin = { id: 'p1', name: 'Plugin Alpha', author: 'A', description: 'desc', category: 'Collector', tags: [], latestVersion: '1.0.0', minHostVersion: '9.0', downloadCount: 100, rating: 4.0, ratingCount: 10, publishedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z', iconUrl: null, verified: false };
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ data: [plugin], total: 1 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    await userEvent.click(screen.getByRole('button', { name: /install plugin alpha/i }));
    expect(mockInstallMutate).toHaveBeenCalledWith({ id: 'p1', name: 'Plugin Alpha' });
  });

  it('opens drawer when plugin card body clicked', async () => {
    const plugin = { id: 'p1', name: 'Plugin Alpha', author: 'A', description: 'desc alpha', category: 'Collector', tags: [], latestVersion: '1.0.0', minHostVersion: '9.0', downloadCount: 100, rating: 4.0, ratingCount: 10, publishedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-06-01T00:00:00Z', iconUrl: null, verified: false };
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ data: [plugin], total: 1 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    await userEvent.click(screen.getByText('desc alpha'));
    expect(screen.getByTestId('drawer')).toHaveAttribute('data-plugin-id', 'p1');
  });

  it('pagination Next button increments page', async () => {
    mockUseMarketplaceSearch.mockReturnValue({
      data:                      makeSearchResult({ total: 40, page: 1, totalPages: 2 }),
      isLoading:                 false,
      isError:                   false,
      isMarketplaceUnconfigured: false,
      error:                     null,
      refetch:                   vi.fn(),
    });
    render(<MarketplacePage />, { wrapper });
    const nextBtn = screen.getByRole('button', { name: /next/i });
    await userEvent.click(nextBtn);
    await waitFor(() => {
      // After clicking Next, page state becomes 2.
      // The hook is called again — verify the call args include page 2.
      const calls = mockUseMarketplaceSearch.mock.calls;
      const lastCall = calls[calls.length - 1][0];
      expect(lastCall.page).toBe(2);
    });
  });
});
```

- [ ] **Step 4: Run MarketplacePage tests**

```bash
cd src/MSOSync.Frontend && npm test -- --testPathPattern=MarketplacePage
```

Expected: 8 tests pass. If any fail, check the mock setup — ensure `vi.mock('./MarketplacePluginDrawer', ...)` is defined before the describe block.

- [ ] **Step 5: Add Marketplace route to `router.tsx`**

Open `src/MSOSync.Frontend/src/app/router.tsx`.

After the existing `import { PluginsPage } from '../features/plugins/PluginsPage';` line, add:

```typescript
import { MarketplacePage } from '../features/plugins/MarketplacePage';
```

Then, inside the `children` array of the `AppLayout` element (after the `administration/plugins` route block), add:

```typescript
{
  path: 'marketplace',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>
      <MarketplacePage />
    </PermissionGuard>
  ),
},
```

Place it directly after the `administration/plugins` block:

```typescript
              {
                path: 'administration/plugins',
                element: (
                  <PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>
                    <PluginsPage />
                  </PermissionGuard>
                ),
              },
              {
                path: 'marketplace',
                element: (
                  <PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>
                    <MarketplacePage />
                  </PermissionGuard>
                ),
              },
```

- [ ] **Step 6: Update `AppLayout.tsx` — import Store, call useUpdateCount, add nav item with badge**

Open `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`.

**6a.** In the lucide-react import block (the one starting with `LayoutDashboard, Network, ...`), add `Store` to the list:

```typescript
import {
  LayoutDashboard,
  Network,
  Server,
  GitBranch,
  Activity,
  AlertTriangle,
  BarChart2,
  Users,
  FileText,
  ShieldCheck,
  Sun,
  Moon,
  LogOut,
  Cpu,
  Settings2,
  Briefcase,
  HeartPulse,
  Flag,
  SlidersHorizontal,
  Archive,
  Stethoscope,
  PieChart,
  Package,
  Monitor,
  Calendar,
  TrendingUp,
  ShieldAlert,
  Gauge,
  Store,
} from 'lucide-react';
```

**6b.** After the existing import of `NotificationBell`, add the `useUpdateCount` import:

```typescript
import { useUpdateCount } from '../../shared/hooks/useMarketplace';
```

**6c.** Add `Marketplace` to the Administration `NAV_GROUPS` array. Find the Administration group and add the Marketplace item after `Plugins`:

```typescript
  {
    heading: 'Administration',
    items: [
      { label: 'Users',         path: '/administration/users',         icon: Users,           requiredPermission: PermissionKeys.ManageUsers },
      { label: 'Roles',         path: '/administration/roles',         icon: ShieldCheck,     requiredPermission: PermissionKeys.ManageUsers },
      { label: 'Feature Flags', path: '/administration/feature-flags', icon: Flag,            requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Settings',      path: '/administration/settings',      icon: SlidersHorizontal, requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Retention',     path: '/administration/retention',     icon: Archive,         requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'License',       path: '/administration/license',       icon: FileText },
      { label: 'Diagnostics',   path: '/administration/diagnostics',   icon: Stethoscope,     requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Plugins',       path: '/administration/plugins',       icon: Package,         requiredPermission: PermissionKeys.ManagePlugins },
      { label: 'Marketplace',   path: '/marketplace',                  icon: Store,           requiredPermission: PermissionKeys.ManagePlugins },
    ],
  },
```

**6d.** Inside `AppLayout`, call `useUpdateCount` alongside `usePreferences()`:

Find:
```typescript
  // Prefetch preferences and permissions for the whole session
  usePreferences();
  usePermissions();
```

Replace with:
```typescript
  // Prefetch preferences and permissions for the whole session
  usePreferences();
  usePermissions();
  const updateCount = useUpdateCount();
```

**6e.** Pass `updateCount` into the `NavGroup` that renders the Administration group. The cleanest approach is to pass it as a prop through `NavGroup` and check for the `/marketplace` path inside. Change the `NavGroup` function signature and its call site.

Change `NavGroup` function signature:
```typescript
function NavGroup({
  heading,
  items,
  updateCount = 0,
}: {
  heading:      string | null;
  items:        NavItem[];
  updateCount?: number;
}) {
```

Change the nav render in `AppLayout` to pass `updateCount` to every `NavGroup` (it only affects the Marketplace item):
```typescript
          {NAV_GROUPS.map((g, groupIndex) => (
            <NavGroup key={groupIndex} heading={g.heading} items={g.items} updateCount={updateCount} />
          ))}
```

**6f.** Inside `NavGroup`, replace the existing `visibleItems.map` render to add a badge on the Marketplace nav link:

Replace:
```typescript
      {visibleItems.map(({ label, path, icon: Icon }) => (
        <NavLink
          key={path}
          to={path}
          className={({ isActive }) =>
            cn(
              'flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors',
              isActive
                ? 'bg-neutral-100 dark:bg-neutral-800 text-neutral-900 dark:text-neutral-100 font-medium'
                : 'text-neutral-600 dark:text-neutral-400 hover:bg-neutral-50 dark:hover:bg-neutral-800/50',
            )
          }
        >
          <Icon className="h-4 w-4 shrink-0" />
          {label}
        </NavLink>
      ))}
```

With:
```typescript
      {visibleItems.map(({ label, path, icon: Icon }) => (
        <NavLink
          key={path}
          to={path}
          className={({ isActive }) =>
            cn(
              'flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors',
              isActive
                ? 'bg-neutral-100 dark:bg-neutral-800 text-neutral-900 dark:text-neutral-100 font-medium'
                : 'text-neutral-600 dark:text-neutral-400 hover:bg-neutral-50 dark:hover:bg-neutral-800/50',
            )
          }
        >
          <Icon className="h-4 w-4 shrink-0" />
          {label}
          {path === '/marketplace' && updateCount > 0 && (
            <span className="ml-auto flex h-4 w-4 items-center justify-center rounded-full bg-red-500 text-[10px] font-bold text-white">
              {updateCount > 99 ? '99+' : updateCount}
            </span>
          )}
        </NavLink>
      ))}
```

- [ ] **Step 7: Verify TypeScript compiles**

```bash
cd src/MSOSync.Frontend && npx tsc --noEmit
```

Expected: no errors. Fix any type mismatches before committing.

- [ ] **Step 8: Commit**

```bash
git add \
  src/MSOSync.Frontend/src/features/plugins/MarketplacePluginDrawer.tsx \
  src/MSOSync.Frontend/src/features/plugins/MarketplacePage.tsx \
  src/MSOSync.Frontend/src/features/plugins/MarketplacePage.test.tsx \
  src/MSOSync.Frontend/src/app/router.tsx \
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git commit -m "feat(2C.3-T4): add MarketplacePage, drawer, route, and nav badge"
```
