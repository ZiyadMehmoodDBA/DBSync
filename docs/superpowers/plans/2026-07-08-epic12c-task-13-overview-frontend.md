# Task 13: Overview Page Frontend

**Epic:** 12C System Administration Center
**Depends on:** Task 12 (route `/overview` registered), backend `GET /api/v1/system/overview` endpoint
**Blocks:** Nothing — standalone page

---

## Goal

Build the Overview page: a single screen giving an ADMIN or OPERATOR a health snapshot, actionable warnings, quick-action buttons, a live activity feed, and a compact system info strip. The page auto-refreshes via SignalR and a manual refresh button.

---

## Step 1 — Read existing API client to confirm apiFetch signature

- [ ] Open `src/MSOSync.Frontend/src/shared/api/client.ts`. Note the exact export name and signature of the fetch helper (it may be `apiFetch`, `apiClient`, or similar). Use that exact name in all new files.

---

## Step 2 — Read existing hooks for query pattern

- [ ] Open one existing hook, e.g. `src/MSOSync.Frontend/src/shared/hooks/useAudit.ts`. Note the import paths for `useQuery` and `useQueryClient`. Use identical import paths in new hook files.

---

## Step 3 — Read existing eventRouter.ts

- [ ] Open `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`. Note the switch-case structure. You will add one new case at the end.

---

## Step 4 — Create types file

- [ ] Create `src/MSOSync.Frontend/src/shared/types/system.ts` with the following content:

```typescript
export type HealthLevel = 'Healthy' | 'Degraded' | 'Critical' | 'Unknown';
export type WarningSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

export interface HealthSummaryDto {
  clusterHealth: HealthLevel;
  workerHealth: HealthLevel;
  nodeHealth: HealthLevel;
}

export interface OperationsSummaryDto {
  activeJobCount: number;
  pendingJobCount: number;
  failedJobCount: number;
}

export interface NodesSummaryDto {
  totalNodes: number;
  activeNodes: number;
  driftedNodes: number;
  pendingRegistrations: number;
}

export interface ActionableWarningDto {
  warningId: string;
  severity: WarningSeverity;
  title: string;
  description: string;
  targetRoute: string;
}

export interface RecentEventDto {
  auditId: number;
  occurredAt: string;
  category: string;
  summary: string;
  correlationId: string | null;
  actorUsername: string | null;
}

export interface OverviewDto {
  health: HealthSummaryDto;
  operations: OperationsSummaryDto;
  nodes: NodesSummaryDto;
  warnings: ActionableWarningDto[];
  recentEvents: RecentEventDto[];
  lastRefreshedAt: string;
}

export interface SystemInfoDto {
  appVersion: string;
  buildDate: string | null;
  gitCommit: string | null;
  dotnetRuntime: string;
  os: string;
  edition: string;
  environment: string;
  serverTime: string;
  processUptimeSeconds: number;
  databaseMigrationVersion: string | null;
}

export interface WorkerStateType =
  | 'Running'
  | 'Idle'
  | 'Warning'
  | 'Failed'
  | 'Delayed'
  | 'Disabled';

export interface WorkerTickDto {
  tickId: number;
  startedAt: string;
  completedAt: string | null;
  durationMs: number | null;
  trigger: string;
  isSuccess: boolean;
  errorMessage: string | null;
}

export interface WorkerStatusDto {
  workerId: string;
  workerName: string;
  workerState: WorkerStateType;
  lastRunAt: string | null;
  nextExpectedAt: string | null;
  avgDurationMs: number | null;
  executionCount: number;
  failureCount: number;
  lastFailureAt: string | null;
  successRatePct: number | null;
  maxDurationMs: number | null;
  recentTicks: WorkerTickDto[];
}

export interface HealthContributionDto {
  contributor: string;
  level: HealthLevel;
  detail: string | null;
  latencyMs: number | null;
}
```

Note: The type alias `WorkerStateType` uses `=` which is invalid TypeScript syntax for a union. Write it as a `type` declaration:

```typescript
export type WorkerStateType =
  | 'Running'
  | 'Idle'
  | 'Warning'
  | 'Failed'
  | 'Delayed'
  | 'Disabled';
```

---

## Step 5 — Create system.ts API file

- [ ] Create `src/MSOSync.Frontend/src/shared/api/system.ts`:

```typescript
import { apiFetch } from './client';
import type {
  OverviewDto,
  SystemInfoDto,
  WorkerStatusDto,
  HealthContributionDto,
} from '../types/system';

export async function fetchOverview(): Promise<OverviewDto> {
  return apiFetch('/api/v1/system/overview');
}

export async function fetchSystemInfo(): Promise<SystemInfoDto> {
  return apiFetch('/api/v1/system/info');
}

export async function fetchWorkers(): Promise<WorkerStatusDto[]> {
  return apiFetch('/api/v1/system/workers');
}

export async function fetchSystemHealth(): Promise<HealthContributionDto[]> {
  return apiFetch('/api/v1/system/health');
}

export const systemKeys = {
  overview: ['system', 'overview'] as const,
  info: ['system', 'info'] as const,
  workers: ['system', 'workers'] as const,
  health: ['system', 'health'] as const,
};
```

---

## Step 6 — Create useOverview.ts hook

- [ ] Create `src/MSOSync.Frontend/src/shared/hooks/useOverview.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { fetchOverview } from '../api/system';
import { systemKeys } from '../api/system';

export function useOverview() {
  return useQuery({
    queryKey: systemKeys.overview,
    queryFn: fetchOverview,
    staleTime: 5_000,
    refetchOnWindowFocus: true,
  });
}
```

---

## Step 7 — Create OverviewHealthBar component

- [ ] Create `src/MSOSync.Frontend/src/features/overview/components/OverviewHealthBar.tsx`:

```typescript
import { Badge } from '@/shared/components/ui/badge';
import { Button } from '@/shared/components/ui/button';
import { RefreshCw } from 'lucide-react';
import type { HealthSummaryDto, OperationsSummaryDto, HealthLevel } from '@/shared/types/system';

function healthColor(level: HealthLevel): string {
  switch (level) {
    case 'Healthy':  return 'bg-green-100 text-green-800 border-green-200';
    case 'Degraded': return 'bg-yellow-100 text-yellow-800 border-yellow-200';
    case 'Critical': return 'bg-red-100 text-red-800 border-red-200';
    default:         return 'bg-gray-100 text-gray-600 border-gray-200';
  }
}

interface Props {
  health: HealthSummaryDto;
  operations: OperationsSummaryDto;
  lastRefreshedAt: string;
  onRefresh: () => void;
  isRefreshing: boolean;
}

export function OverviewHealthBar({ health, operations, lastRefreshedAt, onRefresh, isRefreshing }: Props) {
  const refreshedDate = new Date(lastRefreshedAt);

  return (
    <div className="flex items-center gap-4 rounded-lg border bg-card px-4 py-3">
      <Badge className={`border ${healthColor(health.clusterHealth)}`}>
        Cluster: {health.clusterHealth}
      </Badge>
      <Badge className={`border ${healthColor(health.workerHealth)}`}>
        Workers: {health.workerHealth}
      </Badge>
      <Badge className={`border ${healthColor(health.nodeHealth)}`}>
        Nodes: {health.nodeHealth}
      </Badge>

      <div className="ml-4 text-sm text-muted-foreground">
        Active jobs: <span className="font-medium text-foreground">{operations.activeJobCount}</span>
      </div>

      <div className="ml-auto flex items-center gap-2 text-xs text-muted-foreground">
        <span>Last refreshed {refreshedDate.toLocaleTimeString()}</span>
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={onRefresh}
          disabled={isRefreshing}
          title="Refresh overview"
        >
          <RefreshCw className={`h-3.5 w-3.5 ${isRefreshing ? 'animate-spin' : ''}`} />
        </Button>
      </div>
    </div>
  );
}
```

---

## Step 8 — Create OverviewActionCards component

- [ ] Create `src/MSOSync.Frontend/src/features/overview/components/OverviewActionCards.tsx`:

```typescript
import { useNavigate } from 'react-router-dom';
import { Card, CardHeader, CardContent } from '@/shared/components/ui/card';
import { Badge } from '@/shared/components/ui/badge';
import { Button } from '@/shared/components/ui/button';
import { AlertTriangle, AlertCircle, Info } from 'lucide-react';
import type { ActionableWarningDto, WarningSeverity } from '@/shared/types/system';

function severityColor(s: WarningSeverity): string {
  switch (s) {
    case 'Critical': return 'bg-red-100 text-red-800 border-red-200';
    case 'High':     return 'bg-orange-100 text-orange-800 border-orange-200';
    case 'Medium':   return 'bg-yellow-100 text-yellow-800 border-yellow-200';
    case 'Low':      return 'bg-blue-100 text-blue-800 border-blue-200';
  }
}

function severityIcon(s: WarningSeverity) {
  switch (s) {
    case 'Critical':
    case 'High':   return <AlertCircle className="h-4 w-4" />;
    case 'Medium': return <AlertTriangle className="h-4 w-4" />;
    case 'Low':    return <Info className="h-4 w-4" />;
  }
}

const SEVERITY_ORDER: Record<WarningSeverity, number> = {
  Critical: 0,
  High: 1,
  Medium: 2,
  Low: 3,
};

interface Props {
  warnings: ActionableWarningDto[];
}

export function OverviewActionCards({ warnings }: Props) {
  const navigate = useNavigate();
  const sorted = [...warnings].sort(
    (a, b) => SEVERITY_ORDER[a.severity] - SEVERITY_ORDER[b.severity]
  );

  if (sorted.length === 0) {
    return (
      <div className="rounded-lg border border-dashed px-4 py-6 text-center text-sm text-muted-foreground">
        No actionable warnings — system is operating normally.
      </div>
    );
  }

  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {sorted.map((w) => (
        <Card key={w.warningId} className="border-l-4"
          style={{ borderLeftColor: w.severity === 'Critical' ? '#ef4444'
                                  : w.severity === 'High'     ? '#f97316'
                                  : w.severity === 'Medium'   ? '#eab308'
                                  :                             '#3b82f6' }}>
          <CardHeader className="pb-1 pt-3 px-4">
            <div className="flex items-center justify-between">
              <Badge className={`border text-xs ${severityColor(w.severity)}`}>
                <span className="mr-1">{severityIcon(w.severity)}</span>
                {w.severity}
              </Badge>
            </div>
            <p className="text-sm font-semibold leading-tight mt-1">{w.title}</p>
          </CardHeader>
          <CardContent className="px-4 pb-3">
            <p className="text-xs text-muted-foreground mb-3">{w.description}</p>
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => navigate(w.targetRoute)}
            >
              Open →
            </Button>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
```

---

## Step 9 — Create OverviewQuickActions component

- [ ] Create `src/MSOSync.Frontend/src/features/overview/components/OverviewQuickActions.tsx`:

```typescript
import { useNavigate } from 'react-router-dom';
import { Button } from '@/shared/components/ui/button';
import { CheckSquare, AlertOctagon, Diff, PlusCircle, Users } from 'lucide-react';

export function OverviewQuickActions() {
  const navigate = useNavigate();

  const actions = [
    {
      label: 'Approve Registrations',
      icon: <CheckSquare className="h-4 w-4" />,
      onClick: () => navigate('/node-management?tab=pending'),
    },
    {
      label: 'View Failed Jobs',
      icon: <AlertOctagon className="h-4 w-4" />,
      onClick: () => navigate('/operations/jobs?status=Failed'),
    },
    {
      label: 'Open Drift',
      icon: <Diff className="h-4 w-4" />,
      onClick: () => navigate('/operations/nodes?filter=drifted'),
    },
    {
      label: 'Create Node',
      icon: <PlusCircle className="h-4 w-4" />,
      onClick: () => navigate('/node-management?action=create'),
    },
    {
      label: 'View Workers',
      icon: <Users className="h-4 w-4" />,
      onClick: () => navigate('/operations/health'),
    },
  ];

  return (
    <div className="flex flex-wrap gap-2">
      {actions.map((a) => (
        <Button
          key={a.label}
          variant="outline"
          size="sm"
          className="gap-2"
          onClick={a.onClick}
        >
          {a.icon}
          {a.label}
        </Button>
      ))}
    </div>
  );
}
```

---

## Step 10 — Create OverviewActivityFeed component

- [ ] Create `src/MSOSync.Frontend/src/features/overview/components/OverviewActivityFeed.tsx`:

```typescript
import { useNavigate } from 'react-router-dom';
import { Badge } from '@/shared/components/ui/badge';
import { Button } from '@/shared/components/ui/button';
import type { RecentEventDto } from '@/shared/types/system';

const CATEGORY_COLORS: Record<string, string> = {
  Registration:  'bg-purple-100 text-purple-800',
  Lifecycle:     'bg-blue-100 text-blue-800',
  Configuration: 'bg-green-100 text-green-800',
  Operation:     'bg-orange-100 text-orange-800',
  Security:      'bg-red-100 text-red-800',
  System:        'bg-gray-100 text-gray-700',
};

function relativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime();
  const diffSec = Math.floor(diffMs / 1000);
  if (diffSec < 60) return `${diffSec}s ago`;
  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  return `${diffHr}h ago`;
}

interface Props {
  events: RecentEventDto[];
}

export function OverviewActivityFeed({ events }: Props) {
  const navigate = useNavigate();
  const top10 = events.slice(0, 10);

  if (top10.length === 0) {
    return (
      <p className="text-sm text-muted-foreground py-4">No recent activity.</p>
    );
  }

  return (
    <div className="divide-y rounded-lg border">
      {top10.map((ev) => (
        <div key={ev.auditId} className="flex items-start gap-3 px-4 py-3">
          <Badge
            className={`mt-0.5 shrink-0 text-xs ${CATEGORY_COLORS[ev.category] ?? CATEGORY_COLORS['System']}`}
          >
            {ev.category}
          </Badge>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm">{ev.summary}</p>
            {ev.actorUsername && (
              <p className="text-xs text-muted-foreground">{ev.actorUsername}</p>
            )}
          </div>
          <div className="flex shrink-0 items-center gap-2">
            <span className="text-xs text-muted-foreground" title={ev.occurredAt}>
              {relativeTime(ev.occurredAt)}
            </span>
            {ev.correlationId && (
              <Button
                variant="ghost"
                size="sm"
                className="h-6 px-2 text-xs"
                onClick={() =>
                  navigate(`/operations/activity?correlationId=${encodeURIComponent(ev.correlationId!)}`)
                }
              >
                View Correlation →
              </Button>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
```

---

## Step 11 — Create OverviewSystemInfo component

- [ ] Create `src/MSOSync.Frontend/src/features/overview/components/OverviewSystemInfo.tsx`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { fetchSystemInfo } from '@/shared/api/system';
import { systemKeys } from '@/shared/api/system';
import { Badge } from '@/shared/components/ui/badge';

function formatUptime(seconds: number): string {
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  if (h > 24) return `${Math.floor(h / 24)}d ${h % 24}h`;
  return `${h}h ${m}m`;
}

export function OverviewSystemInfo() {
  const { data } = useQuery({
    queryKey: systemKeys.info,
    queryFn: fetchSystemInfo,
    staleTime: 60_000,
  });

  if (!data) return null;

  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-1 rounded-lg border bg-muted/40 px-4 py-2 text-xs text-muted-foreground">
      <span><span className="font-medium text-foreground">v{data.appVersion}</span></span>
      <span>DB migration: {data.databaseMigrationVersion ?? 'N/A'}</span>
      <span>Env: <Badge variant="outline" className="h-4 px-1 text-xs">{data.environment}</Badge></span>
      <span>Uptime: {formatUptime(data.processUptimeSeconds)}</span>
      <span>.NET {data.dotnetRuntime}</span>
      <span>{data.os}</span>
    </div>
  );
}
```

---

## Step 12 — Create OverviewPage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/overview/OverviewPage.tsx`:

```typescript
import { useOverview } from '@/shared/hooks/useOverview';
import { OverviewHealthBar } from './components/OverviewHealthBar';
import { OverviewActionCards } from './components/OverviewActionCards';
import { OverviewQuickActions } from './components/OverviewQuickActions';
import { OverviewActivityFeed } from './components/OverviewActivityFeed';
import { OverviewSystemInfo } from './components/OverviewSystemInfo';

export function OverviewPage() {
  const { data, isLoading, isRefetching, refetch } = useOverview();

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted-foreground">
        Loading overview...
      </div>
    );
  }

  if (!data) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-destructive">
        Failed to load overview. Check API connection.
      </div>
    );
  }

  return (
    <div className="space-y-6 p-6">
      {/* Page heading */}
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Overview</h1>
        <p className="text-sm text-muted-foreground">System health at a glance</p>
      </div>

      {/* Zone A — Health bar */}
      <OverviewHealthBar
        health={data.health}
        operations={data.operations}
        lastRefreshedAt={data.lastRefreshedAt}
        onRefresh={() => refetch()}
        isRefreshing={isRefetching}
      />

      {/* Zone C — Quick actions (above warnings to keep it above the fold) */}
      <section>
        <h2 className="mb-2 text-sm font-semibold text-muted-foreground uppercase tracking-wider">
          Quick Actions
        </h2>
        <OverviewQuickActions />
      </section>

      {/* Zone B — Actionable warnings */}
      {data.warnings.length > 0 && (
        <section>
          <h2 className="mb-2 text-sm font-semibold text-muted-foreground uppercase tracking-wider">
            Requires Attention ({data.warnings.length})
          </h2>
          <OverviewActionCards warnings={data.warnings} />
        </section>
      )}

      {/* Zone D — Activity feed */}
      <section>
        <h2 className="mb-2 text-sm font-semibold text-muted-foreground uppercase tracking-wider">
          Recent Activity
        </h2>
        <OverviewActivityFeed events={data.recentEvents} />
      </section>

      {/* Zone E — System info strip */}
      <OverviewSystemInfo />
    </div>
  );
}
```

---

## Step 13 — Wire OverviewRefreshed event in eventRouter.ts

- [ ] Open `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`. Add one new case inside the switch statement:

```typescript
case 'OverviewRefreshed':
  queryClient.invalidateQueries({ queryKey: systemKeys.overview });
  break;
```

- [ ] Add the import of `systemKeys` at the top of the file alongside existing imports:

```typescript
import { systemKeys } from '../api/system';
```

---

## Step 14 — Create index barrel for the overview feature

- [ ] Create `src/MSOSync.Frontend/src/features/overview/index.ts`:

```typescript
export { OverviewPage } from './OverviewPage';
```

This allows `router.tsx` to import as `@/features/overview/OverviewPage` or from the barrel.

---

## Step 15 — Build and fix TypeScript errors

- [ ] Run:

```powershell
cd src/MSOSync.Frontend && npm run build 2>&1
```

Fix any type mismatches between `OverviewDto` fields and backend API response. If the backend returns camelCase, no changes needed (TypeScript types already use camelCase). If it returns PascalCase, add a camelCase mapping in `fetchOverview`.

---

## Step 16 — Manual smoke test

- [ ] Run `npm run dev`, navigate to `/overview`.
- [ ] Confirm all 5 zones render. If backend is running, verify real data appears.
- [ ] Click the refresh button — confirm spinner appears and data reloads.
- [ ] Click a warning card "Open →" — confirm navigation to the `targetRoute`.
- [ ] Click "View Correlation →" on an activity event — confirm navigation to `/operations/activity?correlationId=...`.

---

## Step 17 — Commit

- [ ] Stage files:

```powershell
git add src/MSOSync.Frontend/src/shared/types/system.ts
git add src/MSOSync.Frontend/src/shared/api/system.ts
git add src/MSOSync.Frontend/src/shared/hooks/useOverview.ts
git add src/MSOSync.Frontend/src/features/overview/
git add src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts
```

- [ ] Commit:

```powershell
git commit -m "feat(12C-13): Overview page with health bar, warnings, activity feed, system info"
```
