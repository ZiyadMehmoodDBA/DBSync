# Task 17: Administration Pages — Feature Flags, Settings, Retention, License, Diagnostics + Dashboard Simplification

**Epic:** 12C System Administration Center
**Depends on:** Task 12 (routes registered), Task 13 (systemKeys, fetchSystemInfo exist), backend `GET /api/v1/parameters`, `PUT /api/v1/parameters/{name}`, `GET /api/v1/system/info`, `GET /api/v1/system/health`
**Blocks:** Nothing — standalone pages

---

## Goal

Build five administration pages that reuse existing parameter and system APIs. Simplify `DashboardPage` to an executive summary view. All pages are gated by the appropriate `PermissionGuard` in the router (already wired in Task 12).

---

## Step 1 — Read existing parameters API

- [ ] Search for the existing parameters fetch function:

```powershell
Get-ChildItem -Recurse -Path src/MSOSync.Frontend/src -Include "*.ts","*.tsx" | Select-String "fetchParameters\|api/v1/parameters" | Select-Object -First 5
```

Note the exact function name, file path, and `ParameterDto` type shape (especially: `name`, `displayName`, `value`, `category`, `description`, `valueType`, `requiresRestart`, `validationMin`, `validationMax` fields — and whether they are camelCase). Open the types file and copy the exact field names. Do not redefine these types; import from wherever they already exist.

---

## Step 2 — Read DashboardPage.tsx

- [ ] Open `src/MSOSync.Frontend/src/features/dashboard/DashboardPage.tsx`. Note all the data it fetches and all the card sections it renders. You will remove actionable cards and keep only KPI counts + recent activity.

---

## Step 3 — Confirm Switch component exists (for toggle)

- [ ] Check:

```powershell
Test-Path src/MSOSync.Frontend/src/shared/components/ui/switch.tsx
```

If it returns `False`, the Switch component is not installed. Install it:

```powershell
cd src/MSOSync.Frontend && npx shadcn@latest add switch
```

---

## Step 4 — Create FeatureFlagsPage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/administration/feature-flags/FeatureFlagsPage.tsx`:

```typescript
import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from '@/shared/api/client';
import { Card, CardContent, CardHeader } from '@/shared/components/ui/card';
import { Switch } from '@/shared/components/ui/switch';
import { Badge } from '@/shared/components/ui/badge';
import { Input } from '@/shared/components/ui/input';

// Import the existing ParameterDto type. Adjust import path to match where it lives in the project.
// Common paths: @/shared/types/parameters, @/features/parameters/types, @/shared/api/parameters
// Read the project to find the exact path in Step 1.
import type { ParameterDto } from '@/shared/types/parameters';

const PARAM_KEYS = {
  flags: ['parameters', 'feature-flags'] as const,
};

async function fetchFeatureFlags(): Promise<ParameterDto[]> {
  return apiFetch('/api/v1/parameters?category=FeatureFlag');
}

async function updateParameter(name: string, value: string): Promise<void> {
  await apiFetch(`/api/v1/parameters/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value }),
  });
}

export function FeatureFlagsPage() {
  const qc = useQueryClient();
  const [search, setSearch] = useState('');

  const { data = [], isLoading } = useQuery({
    queryKey: PARAM_KEYS.flags,
    queryFn: fetchFeatureFlags,
    staleTime: 30_000,
  });

  const mutation = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameter(name, value),
    onSuccess: () => qc.invalidateQueries({ queryKey: PARAM_KEYS.flags }),
  });

  const filtered = data.filter((p) =>
    p.displayName?.toLowerCase().includes(search.toLowerCase()) ||
    p.name.toLowerCase().includes(search.toLowerCase())
  );

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Feature Flags</h1>
        <p className="text-sm text-muted-foreground">
          Toggle experimental features. Changes take effect immediately unless marked "Restart Required".
        </p>
      </div>

      <div className="flex items-center gap-3">
        <Input
          placeholder="Search flags…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="max-w-xs"
        />
        <span className="text-xs text-muted-foreground">{filtered.length} flags</span>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {filtered.map((p) => {
            const isEnabled = p.value === 'true' || p.value === '1';
            const isPending =
              mutation.isPending && mutation.variables?.name === p.name;

            return (
              <Card key={p.name}>
                <CardHeader className="pb-1 pt-4 px-4">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-sm font-semibold leading-tight truncate">
                        {p.displayName ?? p.name}
                      </p>
                      <p className="text-xs font-mono text-muted-foreground truncate">{p.name}</p>
                    </div>
                    <Switch
                      checked={isEnabled}
                      disabled={isPending}
                      onCheckedChange={(checked) =>
                        mutation.mutate({ name: p.name, value: checked ? 'true' : 'false' })
                      }
                    />
                  </div>
                </CardHeader>
                <CardContent className="px-4 pb-4">
                  {p.description && (
                    <p className="text-xs text-muted-foreground mb-2">{p.description}</p>
                  )}
                  <div className="flex items-center gap-2">
                    {p.requiresRestart ? (
                      <Badge variant="outline" className="text-xs text-yellow-700 border-yellow-300 bg-yellow-50">
                        ⚠ Restart Required
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-xs text-green-700 border-green-300 bg-green-50">
                        ✓ Live
                      </Badge>
                    )}
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
```

---

## Step 5 — Create SettingsPage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/administration/settings/SettingsPage.tsx`:

```typescript
import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from '@/shared/api/client';
import { Card, CardContent, CardHeader } from '@/shared/components/ui/card';
import { Button } from '@/shared/components/ui/button';
import { Input } from '@/shared/components/ui/input';
import { Badge } from '@/shared/components/ui/badge';
import type { ParameterDto } from '@/shared/types/parameters';

// Adjust ParameterDto import path to match project (see Step 1).

const EXCLUDED_CATEGORIES = ['FeatureFlag', 'Retention'];

const SETTINGS_KEYS = {
  all: ['parameters', 'settings'] as const,
};

async function fetchSettings(): Promise<ParameterDto[]> {
  // Fetch all parameters; filter out FeatureFlag and Retention on client side
  return apiFetch('/api/v1/parameters');
}

async function updateParameter(name: string, value: string): Promise<void> {
  await apiFetch(`/api/v1/parameters/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value }),
  });
}

function groupByCategory(params: ParameterDto[]): Record<string, ParameterDto[]> {
  return params.reduce<Record<string, ParameterDto[]>>((acc, p) => {
    const cat = p.category ?? 'General';
    if (!acc[cat]) acc[cat] = [];
    acc[cat].push(p);
    return acc;
  }, {});
}

export function SettingsPage() {
  const qc = useQueryClient();

  const { data = [], isLoading } = useQuery({
    queryKey: SETTINGS_KEYS.all,
    queryFn: fetchSettings,
    staleTime: 30_000,
    select: (all: ParameterDto[]) =>
      all.filter((p) => !EXCLUDED_CATEGORIES.includes(p.category ?? '')),
  });

  const mutation = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameter(name, value),
    onSuccess: () => qc.invalidateQueries({ queryKey: SETTINGS_KEYS.all }),
  });

  // Per-parameter local state for edited values
  const [edits, setEdits] = useState<Record<string, string>>({});
  const grouped = groupByCategory(data);

  return (
    <div className="space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Settings</h1>
        <p className="text-sm text-muted-foreground">System configuration parameters grouped by category.</p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        Object.entries(grouped).map(([category, params]) => (
          <section key={category}>
            <h2 className="mb-3 text-sm font-semibold uppercase tracking-wider text-muted-foreground">
              {category}
            </h2>
            <div className="space-y-2">
              {params.map((p) => {
                const currentEdit = edits[p.name] ?? p.value ?? '';
                const isDirty = currentEdit !== (p.value ?? '');
                const isPending =
                  mutation.isPending && mutation.variables?.name === p.name;

                return (
                  <Card key={p.name}>
                    <CardContent className="flex items-start gap-4 px-4 py-3">
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2 mb-1">
                          <p className="text-sm font-medium">{p.displayName ?? p.name}</p>
                          {p.requiresRestart ? (
                            <Badge variant="outline" className="text-xs text-yellow-700 border-yellow-300 bg-yellow-50">
                              ⚠ Restart Required
                            </Badge>
                          ) : (
                            <Badge variant="outline" className="text-xs text-green-700 border-green-300 bg-green-50">
                              ✓ Live
                            </Badge>
                          )}
                        </div>
                        {p.description && (
                          <p className="text-xs text-muted-foreground mb-1">{p.description}</p>
                        )}
                        {(p.validationMin != null || p.validationMax != null) && (
                          <p className="text-xs text-muted-foreground">
                            Range:{' '}
                            {p.validationMin != null ? p.validationMin : '—'}
                            {' – '}
                            {p.validationMax != null ? p.validationMax : '—'}
                          </p>
                        )}
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        <Input
                          type={p.valueType === 'Number' ? 'number' : 'text'}
                          value={currentEdit}
                          className="w-32 h-8 text-sm"
                          min={p.validationMin ?? undefined}
                          max={p.validationMax ?? undefined}
                          onChange={(e) =>
                            setEdits((prev) => ({ ...prev, [p.name]: e.target.value }))
                          }
                        />
                        <Button
                          size="sm"
                          className="h-8"
                          disabled={!isDirty || isPending}
                          onClick={() =>
                            mutation.mutate({ name: p.name, value: currentEdit })
                          }
                        >
                          {isPending ? 'Saving…' : 'Save'}
                        </Button>
                      </div>
                    </CardContent>
                  </Card>
                );
              })}
            </div>
          </section>
        ))
      )}
    </div>
  );
}
```

---

## Step 6 — Create RetentionPage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/administration/retention/RetentionPage.tsx`:

```typescript
import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from '@/shared/api/client';
import { Button } from '@/shared/components/ui/button';
import { Input } from '@/shared/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/shared/components/ui/table';
import type { ParameterDto } from '@/shared/types/parameters';

const RETENTION_KEYS = {
  all: ['parameters', 'retention'] as const,
};

async function fetchRetentionParams(): Promise<ParameterDto[]> {
  return apiFetch('/api/v1/parameters?category=Retention');
}

async function updateParameter(name: string, value: string): Promise<void> {
  await apiFetch(`/api/v1/parameters/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value }),
  });
}

export function RetentionPage() {
  const qc = useQueryClient();
  const [edits, setEdits] = useState<Record<string, string>>({});

  const { data = [], isLoading } = useQuery({
    queryKey: RETENTION_KEYS.all,
    queryFn: fetchRetentionParams,
    staleTime: 60_000,
  });

  const mutation = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameter(name, value),
    onSuccess: () => qc.invalidateQueries({ queryKey: RETENTION_KEYS.all }),
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Retention Policies</h1>
        <p className="text-sm text-muted-foreground">
          Configure how long historical data is kept. Lower values reduce storage but limit audit history.
        </p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Policy</TableHead>
                <TableHead>Description</TableHead>
                <TableHead className="w-[200px]">Value</TableHead>
                <TableHead className="w-[80px]" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((p) => {
                const currentEdit = edits[p.name] ?? p.value ?? '';
                const isDirty = currentEdit !== (p.value ?? '');
                const isPending =
                  mutation.isPending && mutation.variables?.name === p.name;

                return (
                  <TableRow key={p.name}>
                    <TableCell>
                      <p className="text-sm font-medium">{p.displayName ?? p.name}</p>
                      <p className="text-xs font-mono text-muted-foreground">{p.name}</p>
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground max-w-[280px]">
                      {p.description ?? '—'}
                    </TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        value={currentEdit}
                        min={1}
                        className="h-8 text-sm"
                        onChange={(e) =>
                          setEdits((prev) => ({ ...prev, [p.name]: e.target.value }))
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <Button
                        size="sm"
                        className="h-8"
                        disabled={!isDirty || isPending}
                        onClick={() =>
                          mutation.mutate({ name: p.name, value: currentEdit })
                        }
                      >
                        {isPending ? '…' : 'Save'}
                      </Button>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}
```

---

## Step 7 — Create LicensePage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/administration/license/LicensePage.tsx`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { fetchSystemInfo } from '@/shared/api/system';
import { systemKeys } from '@/shared/api/system';
import { Card, CardContent, CardHeader } from '@/shared/components/ui/card';
import { Badge } from '@/shared/components/ui/badge';
import { format, parseISO } from 'date-fns';

function formatUptime(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  if (h > 24) return `${Math.floor(h / 24)}d ${h % 24}h ${m}m`;
  return `${h}h ${m}m ${s}s`;
}

function fmtDate(iso: string | null): string {
  if (!iso) return '—';
  try { return format(parseISO(iso), 'MMM d, yyyy HH:mm'); } catch { return iso; }
}

export function LicensePage() {
  const { data, isLoading } = useQuery({
    queryKey: systemKeys.info,
    queryFn: fetchSystemInfo,
    staleTime: 60_000,
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">License &amp; System Info</h1>
        <p className="text-sm text-muted-foreground">
          Application version, runtime details, and edition information.
        </p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : !data ? (
        <p className="text-sm text-destructive">Failed to load system info.</p>
      ) : (
        <Card className="max-w-2xl">
          <CardHeader className="pb-2 pt-4 px-6">
            <div className="flex items-center gap-3">
              <div>
                <p className="text-2xl font-bold">MSOSync CE</p>
                <p className="text-sm text-muted-foreground">Community Edition</p>
              </div>
              <div className="ml-auto flex gap-2">
                <Badge className="bg-blue-100 text-blue-800 border-blue-200">
                  {data.edition}
                </Badge>
                <Badge
                  className={
                    data.environment === 'Production'
                      ? 'bg-red-100 text-red-800 border-red-200'
                      : 'bg-gray-100 text-gray-700 border-gray-200'
                  }
                >
                  {data.environment}
                </Badge>
              </div>
            </div>
          </CardHeader>
          <CardContent className="px-6 pb-6">
            <dl className="grid grid-cols-2 gap-x-8 gap-y-3 text-sm">
              <div>
                <dt className="text-muted-foreground text-xs">App Version</dt>
                <dd className="font-mono font-medium">{data.appVersion}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Build Date</dt>
                <dd>{fmtDate(data.buildDate)}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Git Commit</dt>
                <dd className="font-mono text-xs truncate" title={data.gitCommit ?? undefined}>
                  {data.gitCommit ? data.gitCommit.slice(0, 12) : '—'}
                </dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">.NET Runtime</dt>
                <dd>{data.dotnetRuntime}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">OS</dt>
                <dd>{data.os}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">DB Migration</dt>
                <dd className="font-mono text-xs">{data.databaseMigrationVersion ?? '—'}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Server Time</dt>
                <dd>{fmtDate(data.serverTime)}</dd>
              </div>
              <div>
                <dt className="text-muted-foreground text-xs">Process Uptime</dt>
                <dd>{formatUptime(data.processUptimeSeconds)}</dd>
              </div>
            </dl>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
```

---

## Step 8 — Create DiagnosticsPage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/administration/diagnostics/DiagnosticsPage.tsx`:

```typescript
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { fetchSystemHealth } from '@/shared/api/system';
import { systemKeys } from '@/shared/api/system';
import { Card, CardContent, CardHeader } from '@/shared/components/ui/card';
import { Badge } from '@/shared/components/ui/badge';
import { ChevronRight, Database, Cpu, Activity, Server } from 'lucide-react';
import type { HealthLevel } from '@/shared/types/system';

const LEVEL_COLORS: Record<HealthLevel, string> = {
  Healthy:  'bg-green-100 text-green-800 border-green-200',
  Degraded: 'bg-yellow-100 text-yellow-800 border-yellow-200',
  Critical: 'bg-red-100 text-red-800 border-red-200',
  Unknown:  'bg-gray-100 text-gray-600 border-gray-200',
};

const CONTRIBUTOR_ICON: Record<string, React.ReactNode> = {
  Database:  <Database className="h-5 w-5" />,
  Workers:   <Cpu className="h-5 w-5" />,
  Activity:  <Activity className="h-5 w-5" />,
  API:       <Server className="h-5 w-5" />,
};

const CONTRIBUTOR_NAVIGATE: Record<string, string | null> = {
  Database: '/operations/health',
  Workers:  '/operations/health',
  Activity: '/operations/activity',
  API:      null,
};

export function DiagnosticsPage() {
  const navigate = useNavigate();

  const { data = [], isLoading } = useQuery({
    queryKey: systemKeys.health,
    queryFn: fetchSystemHealth,
    staleTime: 15_000,
    refetchOnWindowFocus: true,
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Diagnostics</h1>
        <p className="text-sm text-muted-foreground">
          Health contributors — click a tile to drill into details.
        </p>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {data.map((c) => {
            const navigateTo = CONTRIBUTOR_NAVIGATE[c.contributor] ?? null;

            return (
              <Card
                key={c.contributor}
                className={navigateTo ? 'cursor-pointer hover:bg-muted/40 transition-colors' : ''}
                onClick={() => navigateTo && navigate(navigateTo)}
              >
                <CardHeader className="pb-1 pt-4 px-4">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 text-muted-foreground">
                      {CONTRIBUTOR_ICON[c.contributor] ?? <Server className="h-5 w-5" />}
                      <p className="text-sm font-semibold text-foreground">{c.contributor}</p>
                    </div>
                    <div className="flex items-center gap-1">
                      <Badge className={`border text-xs ${LEVEL_COLORS[c.level] ?? LEVEL_COLORS['Unknown']}`}>
                        {c.level}
                      </Badge>
                      {navigateTo && <ChevronRight className="h-4 w-4 text-muted-foreground" />}
                    </div>
                  </div>
                </CardHeader>
                <CardContent className="px-4 pb-4">
                  {c.detail && (
                    <p className="text-xs text-muted-foreground">{c.detail}</p>
                  )}
                  {c.latencyMs != null && (
                    <p className="text-xs text-muted-foreground mt-0.5">
                      Latency: <span className="font-medium text-foreground">{c.latencyMs}ms</span>
                    </p>
                  )}
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
```

---

## Step 9 — Simplify DashboardPage.tsx

- [ ] Open `src/MSOSync.Frontend/src/features/dashboard/DashboardPage.tsx`.

Remove the following sections (if they exist):
- Any card showing "Pending Registrations" with an action button
- Any card showing "Drift Warnings" with a "Review" or "Open" action button
- Any section titled "Actionable Items" or similar

Keep the following sections:
- Total node count stat tile
- Active node count stat tile
- Today's sync volume stat tile (if present)
- Success rate percentage (if present)
- Recent activity list (last 5 events, keep but remove any "View Correlation →" links — those move to Task 16)
- Quick links or navigation shortcuts (keep)

Add the subtitle "Executive Summary" under the page heading. The exact code to add:

Find the existing `<h1>` heading line (it will say something like `<h1>Dashboard</h1>` or similar). Below it, add:

```typescript
<p className="text-sm text-muted-foreground">Executive Summary</p>
```

---

## Step 10 — Create barrel indexes for all new administration pages

- [ ] Create `src/MSOSync.Frontend/src/features/administration/feature-flags/index.ts`:

```typescript
export { FeatureFlagsPage } from './FeatureFlagsPage';
```

- [ ] Create `src/MSOSync.Frontend/src/features/administration/settings/index.ts`:

```typescript
export { SettingsPage } from './SettingsPage';
```

- [ ] Create `src/MSOSync.Frontend/src/features/administration/retention/index.ts`:

```typescript
export { RetentionPage } from './RetentionPage';
```

- [ ] Create `src/MSOSync.Frontend/src/features/administration/license/index.ts`:

```typescript
export { LicensePage } from './LicensePage';
```

- [ ] Create `src/MSOSync.Frontend/src/features/administration/diagnostics/index.ts`:

```typescript
export { DiagnosticsPage } from './DiagnosticsPage';
```

---

## Step 11 — Fix import paths for ParameterDto

- [ ] In `FeatureFlagsPage.tsx`, `SettingsPage.tsx`, and `RetentionPage.tsx`, find the actual import path for `ParameterDto`. Run:

```powershell
Get-ChildItem -Recurse -Path src/MSOSync.Frontend/src -Include "*.ts","*.tsx" | Select-String "export.*ParameterDto\|interface ParameterDto" | Select-Object -First 3
```

Replace the placeholder comment `// Adjust ParameterDto import path to match project` and the placeholder import with the real path found.

---

## Step 12 — Build check

- [ ] Run:

```powershell
cd src/MSOSync.Frontend && npm run build 2>&1
```

Common issues:
- `Switch` import from `@/shared/components/ui/switch` — if the file is named differently, adjust
- `Table` components may be at a different shadcn path — check existing table usage in the project
- `ParameterDto` fields (`requiresRestart`, `validationMin`, `validationMax`, `valueType`) — if these are named differently in the actual type, adjust field access accordingly. The `valueType` field might be `type`, `dataType`, etc.

---

## Step 13 — Manual smoke test

- [ ] Navigate to `/administration/feature-flags` — verify cards grid with toggles.
- [ ] Toggle a flag ON — verify the Switch updates and the mutation fires. Check network tab for `PUT /api/v1/parameters/{name}`.
- [ ] Navigate to `/administration/settings` — verify grouped parameter sections. Edit a value and click Save.
- [ ] Navigate to `/administration/retention` — verify table with editable number inputs.
- [ ] Navigate to `/administration/license` — verify system info card with all fields populated.
- [ ] Navigate to `/administration/diagnostics` — verify health contributor tiles. Click a tile that has a navigate target — confirm navigation.
- [ ] Navigate to `/dashboard/summary` — verify actionable cards are gone and "Executive Summary" subtitle appears.

---

## Step 14 — Commit

- [ ] Stage files:

```powershell
git add src/MSOSync.Frontend/src/features/administration/
git add src/MSOSync.Frontend/src/features/dashboard/DashboardPage.tsx
```

- [ ] Commit:

```powershell
git commit -m "feat(12C-17): Admin pages (FeatureFlags, Settings, Retention, License, Diagnostics), Dashboard simplification"
```
