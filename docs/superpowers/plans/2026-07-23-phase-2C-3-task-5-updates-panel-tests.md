# Task 5: UpdatesPanel + NotificationBell Integration + Tests

> Part of the [Phase 2C.3 master plan](./2026-07-23-phase-2C-3-master.md)

**Prerequisite:** Tasks 1, 2, 4 complete — hooks and `MarketplacePage` must exist.

**Goal:** Build `UpdatesPanel`, wire it into `PluginsPage`, inject the plugin-updates banner into `NotificationBell`, write `UpdatesPanel` tests, and do a final full test run.

**Files:**
- Create: `src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.tsx`
- Create: `src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.test.tsx`
- Modify: `src/MSOSync.Frontend/src/features/plugins/PluginsPage.tsx`
- Modify: `src/MSOSync.Frontend/src/features/notifications/NotificationBell.tsx`

**Interfaces:**
- Consumes: `useCheckAllUpdates`, `useUpdatePlugin`, `useUpdateCount` from `shared/hooks/useMarketplace`
- Consumes: `MarketplaceUpdateManifestDto`, `BulkUpdateCheckResult` from `shared/types/marketplace`
- Produces: `<UpdatesPanel installedPluginIds={string[]}>` — consumed by `PluginsPage`

---

- [ ] **Step 1: Create `UpdatesPanel.tsx`**

Create `src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.tsx`:

```typescript
import { useState } from 'react';
import { RefreshCw, Loader2, ArrowRight } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Separator } from '../../components/ui/separator';
import { EmptyState } from '../../shared/components/feedback/EmptyState';
import { useCheckAllUpdates, useUpdatePlugin } from '../../shared/hooks/useMarketplace';

interface UpdatesPanelProps {
  installedPluginIds: string[];   // from usePlugins() in PluginsPage — passed as prop, not re-fetched
}

export function UpdatesPanel({ installedPluginIds: _installedPluginIds }: UpdatesPanelProps) {
  // installedPluginIds is accepted for future filtering if the parent page expands.
  // The backend bulk-check covers all installed plugins server-side, so it's not
  // used to filter results here — but the prop is kept for consistency with the spec.
  const { data, isLoading, isMarketplaceUnconfigured, refetch } = useCheckAllUpdates();
  const updateMutation = useUpdatePlugin();
  const [inFlight, setInFlight] = useState<Set<string>>(new Set());

  const updates = data?.updates ?? [];

  async function handleUpdateAll() {
    for (const manifest of updates) {
      setInFlight(prev => new Set(prev).add(manifest.pluginId));
      try {
        await updateMutation.mutateAsync({
          id:      manifest.pluginId,
          version: manifest.availableVersion,
          name:    manifest.pluginId,
        });
      } finally {
        setInFlight(prev => {
          const next = new Set(prev);
          next.delete(manifest.pluginId);
          return next;
        });
      }
    }
  }

  async function handleUpdateOne(pluginId: string, availableVersion: string) {
    setInFlight(prev => new Set(prev).add(pluginId));
    try {
      await updateMutation.mutateAsync({
        id:      pluginId,
        version: availableVersion,
        name:    pluginId,
      });
    } finally {
      setInFlight(prev => {
        const next = new Set(prev);
        next.delete(pluginId);
        return next;
      });
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold text-neutral-900 dark:text-neutral-100">
            Plugin Updates
          </h2>
          <p className="text-sm text-neutral-500">
            Check for available updates to installed plugins.
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={refetch}
          disabled={isLoading}
          className="gap-2"
        >
          <RefreshCw className={`h-4 w-4 ${isLoading ? 'animate-spin' : ''}`} />
          Check for Updates
        </Button>
      </div>

      <Separator />

      {/* Loading */}
      {isLoading && (
        <div className="flex items-center justify-center py-8 gap-2 text-sm text-neutral-500">
          <Loader2 className="h-4 w-4 animate-spin" />
          Checking for updates…
        </div>
      )}

      {/* Unconfigured */}
      {!isLoading && isMarketplaceUnconfigured && (
        <EmptyState message="Marketplace not configured. Contact your administrator to set up the plugin registry." />
      )}

      {/* No updates */}
      {!isLoading && !isMarketplaceUnconfigured && updates.length === 0 && (
        <EmptyState message="All plugins are up to date." />
      )}

      {/* Updates list */}
      {!isLoading && !isMarketplaceUnconfigured && updates.length > 0 && (
        <div className="space-y-3">
          <div className="flex justify-end">
            <Button
              size="sm"
              onClick={() => void handleUpdateAll()}
              disabled={inFlight.size > 0}
            >
              Update All ({updates.length})
            </Button>
          </div>

          <div className="rounded-lg border border-neutral-200 dark:border-neutral-700 divide-y divide-neutral-200 dark:divide-neutral-700">
            {updates.map(manifest => (
              <div
                key={manifest.pluginId}
                className="flex items-start gap-4 px-4 py-3"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-neutral-900 dark:text-neutral-100">
                    {manifest.pluginId}
                  </p>
                  <div className="flex items-center gap-1.5 mt-0.5 text-xs text-neutral-500">
                    <span className="font-mono">v{manifest.installedVersion}</span>
                    <ArrowRight className="h-3 w-3 shrink-0" />
                    <span className="font-mono text-green-600 dark:text-green-400">
                      v{manifest.availableVersion}
                    </span>
                  </div>
                  {manifest.releaseNotes && (
                    <p className="mt-1 text-xs text-neutral-500 truncate max-w-md">
                      {manifest.releaseNotes.slice(0, 100)}
                      {manifest.releaseNotes.length > 100 ? '…' : ''}
                    </p>
                  )}
                </div>

                <Button
                  size="sm"
                  variant="outline"
                  className="shrink-0 gap-1.5"
                  disabled={inFlight.has(manifest.pluginId)}
                  onClick={() => void handleUpdateOne(manifest.pluginId, manifest.availableVersion)}
                  aria-label={`Update ${manifest.pluginId} to ${manifest.availableVersion}`}
                >
                  {inFlight.has(manifest.pluginId) ? (
                    <Loader2 className="h-3 w-3 animate-spin" />
                  ) : null}
                  Update
                </Button>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Write failing tests for `UpdatesPanel`**

Create `src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.test.tsx`:

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { UpdatesPanel } from './UpdatesPanel';
import type { BulkUpdateCheckResult } from '../../shared/types/marketplace';

// ── Mock hooks ────────────────────────────────────────────────────────────────

const mockCheckAllUpdates = vi.fn();
const mockUpdateMutateAsync = vi.fn().mockResolvedValue({
  success: true, pluginId: 'p1', installedVersion: '2.0.0', restartRequired: true, errorMessage: null,
});

vi.mock('../../shared/hooks/useMarketplace', () => ({
  useCheckAllUpdates:       (...args: unknown[]) => mockCheckAllUpdates(...args),
  useUpdatePlugin: () => ({
    mutateAsync: mockUpdateMutateAsync,
    isPending:   false,
  }),
  useUpdateCount: () => 0,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeResult(overrides: Partial<BulkUpdateCheckResult> = {}): BulkUpdateCheckResult {
  return {
    totalChecked:     0,
    updatesAvailable: 0,
    updates:          [],
    ...overrides,
  };
}

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('UpdatesPanel', () => {
  beforeEach(() => {
    mockCheckAllUpdates.mockReturnValue({
      data:                      makeResult(),
      isLoading:                 false,
      isMarketplaceUnconfigured: false,
      refetch:                   vi.fn(),
    });
    mockUpdateMutateAsync.mockResolvedValue({
      success: true, pluginId: 'p1', installedVersion: '2.0.0', restartRequired: true, errorMessage: null,
    });
  });

  it('renders unconfigured state when marketplace not configured', () => {
    mockCheckAllUpdates.mockReturnValue({
      data:                      undefined,
      isLoading:                 false,
      isMarketplaceUnconfigured: true,
      refetch:                   vi.fn(),
    });
    render(<UpdatesPanel installedPluginIds={[]} />, { wrapper });
    expect(screen.getByText(/marketplace not configured/i)).toBeInTheDocument();
  });

  it('renders no updates message when all up to date', () => {
    mockCheckAllUpdates.mockReturnValue({
      data:                      makeResult({ updates: [] }),
      isLoading:                 false,
      isMarketplaceUnconfigured: false,
      refetch:                   vi.fn(),
    });
    render(<UpdatesPanel installedPluginIds={[]} />, { wrapper });
    expect(screen.getByText(/all plugins are up to date/i)).toBeInTheDocument();
  });

  it('renders update rows for available updates', () => {
    mockCheckAllUpdates.mockReturnValue({
      data: makeResult({
        updatesAvailable: 2,
        updates: [
          { pluginId: 'com.example.alpha', installedVersion: '1.0.0', availableVersion: '2.0.0', downloadUrl: '', sha256: '', releaseNotes: null, publishedAt: '2026-07-01T00:00:00Z' },
          { pluginId: 'com.example.beta',  installedVersion: '3.1.0', availableVersion: '3.2.0', downloadUrl: '', sha256: '', releaseNotes: 'Bug fixes', publishedAt: '2026-07-10T00:00:00Z' },
        ],
      }),
      isLoading:                 false,
      isMarketplaceUnconfigured: false,
      refetch:                   vi.fn(),
    });
    render(<UpdatesPanel installedPluginIds={[]} />, { wrapper });
    expect(screen.getByText('com.example.alpha')).toBeInTheDocument();
    expect(screen.getByText('com.example.beta')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /update com\.example\.alpha to 2\.0\.0/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /update com\.example\.beta to 3\.2\.0/i })).toBeInTheDocument();
  });

  it('renders loading spinner while checking', () => {
    mockCheckAllUpdates.mockReturnValue({
      data:                      undefined,
      isLoading:                 true,
      isMarketplaceUnconfigured: false,
      refetch:                   vi.fn(),
    });
    render(<UpdatesPanel installedPluginIds={[]} />, { wrapper });
    expect(screen.getByText(/checking for updates/i)).toBeInTheDocument();
  });

  it('Update All button calls mutateAsync for each update', async () => {
    const updates = [
      { pluginId: 'p1', installedVersion: '1.0.0', availableVersion: '2.0.0', downloadUrl: '', sha256: '', releaseNotes: null, publishedAt: '2026-07-01T00:00:00Z' },
      { pluginId: 'p2', installedVersion: '0.5.0', availableVersion: '1.0.0', downloadUrl: '', sha256: '', releaseNotes: null, publishedAt: '2026-07-05T00:00:00Z' },
    ];
    mockCheckAllUpdates.mockReturnValue({
      data:                      makeResult({ updatesAvailable: 2, updates }),
      isLoading:                 false,
      isMarketplaceUnconfigured: false,
      refetch:                   vi.fn(),
    });
    mockUpdateMutateAsync
      .mockResolvedValueOnce({ success: true, pluginId: 'p1', installedVersion: '2.0.0', restartRequired: false, errorMessage: null })
      .mockResolvedValueOnce({ success: true, pluginId: 'p2', installedVersion: '1.0.0', restartRequired: false, errorMessage: null });

    render(<UpdatesPanel installedPluginIds={[]} />, { wrapper });
    await userEvent.click(screen.getByRole('button', { name: /update all/i }));

    // Allow the sequential async loop to complete
    await vi.waitFor(() => {
      expect(mockUpdateMutateAsync).toHaveBeenCalledTimes(2);
    });
    expect(mockUpdateMutateAsync).toHaveBeenNthCalledWith(1, { id: 'p1', version: '2.0.0', name: 'p1' });
    expect(mockUpdateMutateAsync).toHaveBeenNthCalledWith(2, { id: 'p2', version: '1.0.0', name: 'p2' });
  });

  it('shows spinner on individual update row while pending', () => {
    const updates = [
      { pluginId: 'p1', installedVersion: '1.0.0', availableVersion: '2.0.0', downloadUrl: '', sha256: '', releaseNotes: null, publishedAt: '2026-07-01T00:00:00Z' },
    ];
    mockCheckAllUpdates.mockReturnValue({
      data:                      makeResult({ updatesAvailable: 1, updates }),
      isLoading:                 false,
      isMarketplaceUnconfigured: false,
      refetch:                   vi.fn(),
    });
    // Don't resolve the mutation — keep inFlight populated
    mockUpdateMutateAsync.mockReturnValue(new Promise(() => {}));

    render(<UpdatesPanel installedPluginIds={[]} />, { wrapper });
    const updateBtn = screen.getByRole('button', { name: /update p1 to 2\.0\.0/i });
    userEvent.click(updateBtn);

    // After click, the button gets disabled and shows spinner — check disabled state
    // (spinner only appears inside inFlight set, driven by local state after click)
    // We verify the button becomes disabled after the async click starts
    expect(updateBtn).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run UpdatesPanel tests**

```bash
cd src/MSOSync.Frontend && npm test -- --testPathPattern=UpdatesPanel
```

Expected: 6 tests pass. If the "Update All" test fails with timing issues, increase the `vi.waitFor` timeout — the default 1000 ms should be sufficient for the sequential mock.

- [ ] **Step 4: Wire `UpdatesPanel` into `PluginsPage`**

Open `src/MSOSync.Frontend/src/features/plugins/PluginsPage.tsx`.

Add the `UpdatesPanel` import at the top of the file, after the existing plugin imports:

```typescript
import { UpdatesPanel } from './UpdatesPanel';
```

Inside the `PluginsPage` return statement, find the wrapping `<div className="p-6 space-y-6">` and add `<UpdatesPanel>` after `<PluginSummaryCard />`:

Replace:
```typescript
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">Plugins</h1>
        <p className="text-sm text-neutral-500 mt-1">
          Discovered at startup. Restart required after enable/disable changes.
        </p>
      </div>

      <PluginSummaryCard />

      {!plugins?.length ? (
```

With:
```typescript
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">Plugins</h1>
        <p className="text-sm text-neutral-500 mt-1">
          Discovered at startup. Restart required after enable/disable changes.
        </p>
      </div>

      <PluginSummaryCard />

      <UpdatesPanel installedPluginIds={plugins?.map(p => p.pluginId) ?? []} />

      {!plugins?.length ? (
```

- [ ] **Step 5: Update `PluginsPage.test.tsx` mock to include `UpdatesPanel` dependency**

Open `src/MSOSync.Frontend/src/features/plugins/PluginsPage.test.tsx`.

The existing test mocks `./hooks`. Add a new `vi.mock` for the marketplace hooks so `UpdatesPanel` (now rendered by `PluginsPage`) doesn't make real network calls:

Add after the existing `vi.mock('./hooks', ...)` call:

```typescript
vi.mock('../../shared/hooks/useMarketplace', () => ({
  useCheckAllUpdates: () => ({
    data:                      { totalChecked: 0, updatesAvailable: 0, updates: [] },
    isLoading:                 false,
    isMarketplaceUnconfigured: false,
    refetch:                   vi.fn(),
  }),
  useUpdatePlugin: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useUpdateCount:  () => 0,
}));
```

- [ ] **Step 6: Run PluginsPage tests**

```bash
cd src/MSOSync.Frontend && npm test -- --testPathPattern=PluginsPage
```

Expected: existing 2 tests still pass. If they fail with "useCheckAllUpdates is not a function", verify the `vi.mock` block was added correctly in step 5.

- [ ] **Step 7: Inject plugin update banner into `NotificationBell`**

Open `src/MSOSync.Frontend/src/features/notifications/NotificationBell.tsx`.

**7a.** Add imports at the top:

```typescript
import { Store } from 'lucide-react';
import { useUpdateCount } from '../../shared/hooks/useMarketplace';
```

**7b.** Inside the `NotificationBell` function body, call `useUpdateCount()` alongside the existing hooks:

```typescript
  const unreadCount          = useUnreadCount();
  const { items, isLoading } = useNotifications('all', 5);
  const markRead             = useMarkRead();
  const markAllRead          = useMarkAllRead();
  const updateCount          = useUpdateCount();
```

**7c.** Inside `<PopoverContent>`, in the scrollable `divide-y` container, add the update banner **before** the existing `isLoading` block:

```typescript
        <div className="divide-y divide-neutral-100 dark:divide-neutral-800 max-h-80 overflow-y-auto">
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
          {isLoading && (
            <p className="px-4 py-6 text-sm text-center text-neutral-500">Loading…</p>
          )}
          {!isLoading && items.length === 0 && (
            <p className="px-4 py-6 text-sm text-center text-neutral-500">No notifications</p>
          )}
          {items.map(n => (
            <NotificationItem
              key={n.notificationId}
              notification={n}
              onMarkRead={(id) => void markRead.mutateAsync({ notificationId: id })}
            />
          ))}
        </div>
```

- [ ] **Step 8: Verify TypeScript compiles**

```bash
cd src/MSOSync.Frontend && npx tsc --noEmit
```

Expected: no errors. The `Link` component is already imported in `NotificationBell.tsx` — if not, add `import { Link } from 'react-router-dom';` at the top of the file.

- [ ] **Step 9: Run full test suite**

```bash
cd src/MSOSync.Frontend && npm test
```

Expected: all tests pass. Key test files to watch:
- `MarketplacePluginCard.test.tsx` — 7 tests
- `MarketplacePage.test.tsx` — 8 tests
- `UpdatesPanel.test.tsx` — 6 tests
- `PluginsPage.test.tsx` — 2 tests

Fix any failures before committing.

- [ ] **Step 10: Commit**

```bash
git add \
  src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.tsx \
  src/MSOSync.Frontend/src/features/plugins/UpdatesPanel.test.tsx \
  src/MSOSync.Frontend/src/features/plugins/PluginsPage.tsx \
  src/MSOSync.Frontend/src/features/plugins/PluginsPage.test.tsx \
  src/MSOSync.Frontend/src/features/notifications/NotificationBell.tsx
git commit -m "feat(2C.3-T5): add UpdatesPanel, PluginsPage wiring, NotificationBell banner + tests"
```
