# Epic 14A — Task 8: Frontend — Plugins Page

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Implement all frontend artifacts for the Plugins admin page: TypeScript types, API client, React Query hooks, `PluginStatusBadge`, `PluginSummaryCard`, `PluginsPage` with expandable rows, route registration at `/administration/plugins`, and sidebar entry. Add `ManagePlugins` permission key.

**Architecture:** Follows existing `features/notifications/` pattern. Reuses `StatusBadge`, `SummaryCard`, `DataGrid` from `src/shared/components/data-display/`. Toast via `sonner` (used by other pages). `useQuery`/`useMutation` from `@tanstack/react-query`. Admin-only route guarded by `PermissionKeys.ManagePlugins`.

**Tech Stack:** React 19 / TypeScript / TanStack Query / Vite / Vitest

## Global Constraints

- Route: `/administration/plugins` (not `/admin/plugins`)
- ADMIN-only: `<PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>`
- Add `ManagePlugins: 'MANAGE_PLUGINS'` to `PermissionKeys` in `src/shared/types/permissions.ts`
- On enable/disable: `toast.info("Plugin {name} {enabled ? 'enabled' : 'disabled'}. Restart required to take effect.")`
- `HostCompatibility` column visible in table (not just in expanded row)
- `PluginSummaryCard` consumed on Overview dashboard (task says add it to overview — scope: add it to the `PluginsPage` summary section for now, not the OverviewPage, to avoid cross-feature coupling)
- No file upload, install, remove UI

## Files

**Create:**
- `src/MSOSync.Frontend/src/features/plugins/types.ts`
- `src/MSOSync.Frontend/src/features/plugins/api.ts`
- `src/MSOSync.Frontend/src/features/plugins/hooks.ts`
- `src/MSOSync.Frontend/src/features/plugins/PluginStatusBadge.tsx`
- `src/MSOSync.Frontend/src/features/plugins/PluginSummaryCard.tsx`
- `src/MSOSync.Frontend/src/features/plugins/PluginsPage.tsx`
- `src/MSOSync.Frontend/src/features/plugins/PluginsPage.test.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/shared/types/permissions.ts` — add `ManagePlugins`
- `src/MSOSync.Frontend/src/shared/queryKeys.ts` — add plugin query keys
- `src/MSOSync.Frontend/src/app/router.tsx` — add `/administration/plugins` route
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add "Plugins" to Administration sidebar

## Interfaces

**Consumes:** `GET /api/v1/plugins`, `GET /api/v1/plugins/summary`, `POST /api/v1/plugins/{id}/enable`, `POST /api/v1/plugins/{id}/disable` (Task 7)

**Produces:** `/administration/plugins` page (consumed by Task 9 integration tests)

---

- [ ] **Step 1: Add `ManagePlugins` to `src/MSOSync.Frontend/src/shared/types/permissions.ts`**

After the `ManageConfigurations` line, add:

```typescript
  ManagePlugins: 'MANAGE_PLUGINS',
```

- [ ] **Step 2: Add plugin query keys to `src/MSOSync.Frontend/src/shared/queryKeys.ts`**

Open the file and add:

```typescript
  plugins: {
    all:     () => ['plugins'] as const,
    detail:  (id: string) => ['plugins', id] as const,
    summary: () => ['plugins', 'summary'] as const,
  },
```

- [ ] **Step 3: Create `src/MSOSync.Frontend/src/features/plugins/types.ts`**

```typescript
export type PluginStatus = 'Discovered' | 'Validated' | 'Loaded' | 'Disabled' | 'Failed';

export interface PluginDto {
  pluginId:          string;
  name:              string;
  version:           string;
  status:            PluginStatus;
  loadDurationMs:    number;
  loadedAt:          string;
  lastError:         string | null;
  failureStage:      string | null;
  hostCompatibility: string;
  capabilities:      string[];
  permissions:       string[];
  dependencies:      string[];
}

export interface PluginSummaryDto {
  total:             number;
  loaded:            number;
  failed:            number;
  disabled:          number;
  startupDurationMs: number;
  lastScanAt:        string | null;
}

export interface PluginManifestDto {
  id:             string;
  name:           string;
  version:        string;
  minHostVersion: string;
  maxHostVersion: string;
  entryAssembly:  string;
  entryType:      string;
  author:         string;
  description:    string;
  permissions:    string[];
  dependencies:   string[];
  capabilities:   string[];
}
```

- [ ] **Step 4: Create `src/MSOSync.Frontend/src/features/plugins/api.ts`**

```typescript
import { client } from '../../shared/api/client';
import type { PluginDto, PluginSummaryDto } from './types';

export async function getPlugins(): Promise<PluginDto[]> {
  const { data } = await client.get<PluginDto[]>('/api/v1/plugins');
  return data;
}

export async function getPluginSummary(): Promise<PluginSummaryDto> {
  const { data } = await client.get<PluginSummaryDto>('/api/v1/plugins/summary');
  return data;
}

export async function enablePlugin(pluginId: string): Promise<void> {
  await client.post(`/api/v1/plugins/${pluginId}/enable`);
}

export async function disablePlugin(pluginId: string): Promise<void> {
  await client.post(`/api/v1/plugins/${pluginId}/disable`);
}
```

- [ ] **Step 5: Create `src/MSOSync.Frontend/src/features/plugins/hooks.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { disablePlugin, enablePlugin, getPluginSummary, getPlugins } from './api';
import { queryKeys } from '../../shared/queryKeys';

export function usePlugins() {
  return useQuery({
    queryKey: queryKeys.plugins.all(),
    queryFn:  getPlugins,
  });
}

export function usePluginSummary() {
  return useQuery({
    queryKey: queryKeys.plugins.summary(),
    queryFn:  getPluginSummary,
  });
}

export function useEnablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ pluginId }: { pluginId: string; name: string }) =>
      enablePlugin(pluginId),
    onSuccess: (_data, { name }) => {
      toast.info(`Plugin "${name}" enabled. Restart required to take effect.`);
      qc.invalidateQueries({ queryKey: queryKeys.plugins.all() });
    },
    onError: () => toast.error('Failed to enable plugin.'),
  });
}

export function useDisablePlugin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ pluginId }: { pluginId: string; name: string }) =>
      disablePlugin(pluginId),
    onSuccess: (_data, { name }) => {
      toast.info(`Plugin "${name}" disabled. Restart required to take effect.`);
      qc.invalidateQueries({ queryKey: queryKeys.plugins.all() });
    },
    onError: () => toast.error('Failed to disable plugin.'),
  });
}
```

- [ ] **Step 6: Create `src/MSOSync.Frontend/src/features/plugins/PluginStatusBadge.tsx`**

```tsx
import { StatusBadge } from '../../../shared/components/data-display/StatusBadge';
import type { StatusVariant } from '../../../shared/utils/status';
import type { PluginStatus } from './types';

function pluginStatusVariant(status: PluginStatus): StatusVariant {
  switch (status) {
    case 'Loaded':     return 'success';
    case 'Failed':     return 'danger';
    case 'Disabled':   return 'neutral';
    case 'Validated':  return 'warning';
    case 'Discovered': return 'warning';
    default:           return 'neutral';
  }
}

interface Props { status: PluginStatus }

export function PluginStatusBadge({ status }: Props) {
  return <StatusBadge status={status} variant={pluginStatusVariant(status)} />;
}
```

- [ ] **Step 7: Create `src/MSOSync.Frontend/src/features/plugins/PluginSummaryCard.tsx`**

```tsx
import { Package } from 'lucide-react';
import { SummaryCard } from '../../../shared/components/data-display/SummaryCard';
import { usePluginSummary } from './hooks';

export function PluginSummaryCard() {
  const { data, isLoading } = usePluginSummary();

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <SummaryCard
        title="Plugins Loaded"
        value={data?.loaded ?? 0}
        subtitle={`of ${data?.total ?? 0} total`}
        icon={Package}
        variant={data && data.failed > 0 ? 'warning' : 'success'}
        loading={isLoading}
      />
      <SummaryCard
        title="Failed"
        value={data?.failed ?? 0}
        icon={Package}
        variant={data && data.failed > 0 ? 'danger' : 'default'}
        loading={isLoading}
      />
      <SummaryCard
        title="Disabled"
        value={data?.disabled ?? 0}
        icon={Package}
        loading={isLoading}
      />
      <SummaryCard
        title="Startup"
        value={data ? `${data.startupDurationMs}ms` : '—'}
        icon={Package}
        loading={isLoading}
      />
    </div>
  );
}
```

- [ ] **Step 8: Create `src/MSOSync.Frontend/src/features/plugins/PluginsPage.tsx`**

```tsx
import { useState } from 'react';
import { ChevronDown, ChevronRight, Package } from 'lucide-react';
import { PluginStatusBadge } from './PluginStatusBadge';
import { PluginSummaryCard } from './PluginSummaryCard';
import { useDisablePlugin, useEnablePlugin, usePlugins } from './hooks';
import type { PluginDto } from './types';
import { Button } from '../../../components/ui/button';
import { ErrorState } from '../../../shared/components/data-display/ErrorState';
import { EmptyState } from '../../../shared/components/data-display/EmptyState';

export function PluginsPage() {
  const { data: plugins, isLoading, isError } = usePlugins();
  const enableMutation  = useEnablePlugin();
  const disableMutation = useDisablePlugin();
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  const toggleExpand = (pluginId: string) => {
    setExpanded(prev => {
      const next = new Set(prev);
      if (next.has(pluginId)) next.delete(pluginId);
      else next.add(pluginId);
      return next;
    });
  };

  if (isLoading) return <div className="p-8 text-center text-neutral-500">Loading plugins…</div>;
  if (isError)   return <ErrorState message="Failed to load plugins." />;

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-neutral-900 dark:text-neutral-100">Plugins</h1>
        <p className="text-sm text-neutral-500 mt-1">
          Discovered at startup. Restart required after enable/disable changes.
        </p>
      </div>

      <PluginSummaryCard />

      {!plugins?.length ? (
        <EmptyState message="No plugins discovered. Place plugin folders in the plugins/ directory." />
      ) : (
        <div className="rounded-lg border border-neutral-200 dark:border-neutral-700 overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-neutral-50 dark:bg-neutral-800">
              <tr>
                <th className="w-8 px-3 py-3" />
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Name</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Version</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Status</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Compatibility</th>
                <th className="px-4 py-3 text-left font-medium text-neutral-600 dark:text-neutral-400">Load Time</th>
                <th className="px-4 py-3 text-right font-medium text-neutral-600 dark:text-neutral-400">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-neutral-200 dark:divide-neutral-700">
              {plugins.map(plugin => (
                <PluginRow
                  key={plugin.pluginId}
                  plugin={plugin}
                  expanded={expanded.has(plugin.pluginId)}
                  onToggle={() => toggleExpand(plugin.pluginId)}
                  onEnable={() => enableMutation.mutate({ pluginId: plugin.pluginId, name: plugin.name })}
                  onDisable={() => disableMutation.mutate({ pluginId: plugin.pluginId, name: plugin.name })}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

interface PluginRowProps {
  plugin:   PluginDto;
  expanded: boolean;
  onToggle: () => void;
  onEnable: () => void;
  onDisable: () => void;
}

function PluginRow({ plugin, expanded, onToggle, onEnable, onDisable }: PluginRowProps) {
  const isDisabled = plugin.status === 'Disabled';

  return (
    <>
      <tr className="hover:bg-neutral-50 dark:hover:bg-neutral-800/50">
        <td className="px-3 py-3">
          <button onClick={onToggle} className="text-neutral-400 hover:text-neutral-600">
            {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
          </button>
        </td>
        <td className="px-4 py-3 font-medium text-neutral-900 dark:text-neutral-100">
          <div className="flex items-center gap-2">
            <Package className="h-4 w-4 text-neutral-400" />
            {plugin.name}
          </div>
          <div className="text-xs text-neutral-400 font-mono">{plugin.pluginId}</div>
        </td>
        <td className="px-4 py-3 text-neutral-600 dark:text-neutral-400 font-mono text-xs">
          {plugin.version}
        </td>
        <td className="px-4 py-3">
          <PluginStatusBadge status={plugin.status} />
        </td>
        <td className="px-4 py-3 text-neutral-600 dark:text-neutral-400 text-xs">
          {plugin.hostCompatibility}
        </td>
        <td className="px-4 py-3 text-neutral-600 dark:text-neutral-400 text-xs">
          {plugin.loadDurationMs}ms
        </td>
        <td className="px-4 py-3 text-right">
          {isDisabled ? (
            <Button size="sm" variant="outline" onClick={onEnable}>Enable</Button>
          ) : (
            <Button size="sm" variant="outline" onClick={onDisable}>Disable</Button>
          )}
        </td>
      </tr>
      {expanded && (
        <tr className="bg-neutral-50/50 dark:bg-neutral-800/30">
          <td colSpan={7} className="px-8 py-4">
            <PluginExpandedDetail plugin={plugin} />
          </td>
        </tr>
      )}
    </>
  );
}

function PluginExpandedDetail({ plugin }: { plugin: PluginDto }) {
  return (
    <div className="space-y-3 text-sm">
      {plugin.lastError && (
        <div className="rounded-md bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 p-3">
          <div className="text-xs font-medium text-red-700 dark:text-red-400 mb-1">
            Failed at stage: {plugin.failureStage}
          </div>
          <div className="text-red-800 dark:text-red-300 font-mono text-xs">{plugin.lastError}</div>
        </div>
      )}
      <div className="grid grid-cols-2 gap-4 text-xs text-neutral-600 dark:text-neutral-400">
        {plugin.dependencies.length > 0 && (
          <div>
            <span className="font-medium">Dependencies:</span>{' '}
            {plugin.dependencies.join(', ')}
          </div>
        )}
        {plugin.capabilities.length > 0 && (
          <div>
            <span className="font-medium">Capabilities:</span>{' '}
            {plugin.capabilities.join(', ')}
          </div>
        )}
        {plugin.permissions.length > 0 && (
          <div>
            <span className="font-medium">Permissions:</span>{' '}
            {plugin.permissions.join(', ')}
          </div>
        )}
        <div>
          <span className="font-medium">Loaded at:</span>{' '}
          {new Date(plugin.loadedAt).toLocaleString()}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 9: Create `src/MSOSync.Frontend/src/features/plugins/PluginsPage.test.tsx`**

```tsx
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';
import { PluginsPage } from './PluginsPage';

vi.mock('./hooks', () => ({
  usePlugins:       () => ({ data: [], isLoading: false, isError: false }),
  usePluginSummary: () => ({ data: null, isLoading: false }),
  useEnablePlugin:  () => ({ mutate: vi.fn() }),
  useDisablePlugin: () => ({ mutate: vi.fn() }),
}));

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('PluginsPage', () => {
  it('renders empty state when no plugins', () => {
    render(<PluginsPage />, { wrapper });
    expect(screen.getByText(/no plugins discovered/i)).toBeInTheDocument();
  });

  it('renders page title', () => {
    render(<PluginsPage />, { wrapper });
    expect(screen.getByText('Plugins')).toBeInTheDocument();
  });
});
```

- [ ] **Step 10: Add route to `src/MSOSync.Frontend/src/app/router.tsx`**

Add import at top:

```typescript
import { PluginsPage } from '../features/plugins/PluginsPage';
```

After the `administration/diagnostics` route, add:

```typescript
{
  path: 'administration/plugins',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>
      <PluginsPage />
    </PermissionGuard>
  ),
},
```

- [ ] **Step 11: Add "Plugins" to Administration sidebar in `AppLayout.tsx`**

In the `NAV_GROUPS` Administration items array, after "Diagnostics", add:

```typescript
{ label: 'Plugins', path: '/administration/plugins', icon: Package, requiredPermission: PermissionKeys.ManagePlugins },
```

Add `Package` to the Lucide imports at the top of the file.

- [ ] **Step 12: Run frontend type check and tests**

```bash
cd src/MSOSync.Frontend
npx tsc --noEmit
npx vitest run src/features/plugins/
```

Expected: 0 type errors, 2 tests pass.

- [ ] **Step 13: Commit**

```bash
git add src/MSOSync.Frontend/src/features/plugins/ src/MSOSync.Frontend/src/shared/types/permissions.ts src/MSOSync.Frontend/src/shared/queryKeys.ts src/MSOSync.Frontend/src/app/router.tsx src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git commit -m "feat(14A-8): Plugins frontend — types, api, hooks, PluginStatusBadge, PluginsPage, route, sidebar"
```
