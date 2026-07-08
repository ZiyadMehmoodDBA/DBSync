# Task 15: Health Page Frontend — WorkerCard + Tick History Chart

**Epic:** 12C System Administration Center
**Depends on:** Task 12 (route `/operations/health` registered), Task 13 (system.ts and systemKeys exist), backend `GET /api/v1/system/workers` and `GET /api/v1/system/health` endpoints
**Blocks:** Nothing — standalone page

---

## Goal

Build the Health page: a workers summary bar, a responsive card grid showing each worker with its state, stats, and an expandable Recharts bar chart of the last 100 ticks, plus a System Health panel showing DB and API contributor tiles.

---

## Step 1 — Check if Recharts is already installed

- [ ] Run:

```powershell
Get-Content src/MSOSync.Frontend/package.json | Select-String "recharts"
```

If `recharts` is NOT listed in dependencies, install it:

```powershell
cd src/MSOSync.Frontend && npm install recharts
```

Recharts is a peer dependency of React. No additional configuration needed.

---

## Step 2 — Read systemKeys and WorkerStatusDto from Task 13 types

- [ ] Open `src/MSOSync.Frontend/src/shared/types/system.ts` (created in Task 13). Verify `WorkerStatusDto`, `WorkerTickDto`, `WorkerStateType`, and `HealthContributionDto` are all exported. If any are missing, add them now (copy from the types defined in Task 13 Step 4).

---

## Step 3 — Read system.ts API file from Task 13

- [ ] Open `src/MSOSync.Frontend/src/shared/api/system.ts` (created in Task 13). Verify `fetchWorkers` and `fetchSystemHealth` are exported. If missing, add:

```typescript
export async function fetchWorkers(): Promise<WorkerStatusDto[]> {
  return apiFetch('/api/v1/system/workers');
}

export async function fetchSystemHealth(): Promise<HealthContributionDto[]> {
  return apiFetch('/api/v1/system/health');
}
```

---

## Step 4 — Create useWorkers.ts hook

- [ ] Create `src/MSOSync.Frontend/src/shared/hooks/useWorkers.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { fetchWorkers } from '../api/system';
import { systemKeys } from '../api/system';

export function useWorkers() {
  return useQuery({
    queryKey: systemKeys.workers,
    queryFn: fetchWorkers,
    staleTime: 10_000,
    refetchOnWindowFocus: true,
  });
}
```

---

## Step 5 — Create WorkerTickChart component

- [ ] Create `src/MSOSync.Frontend/src/features/operations/health/components/WorkerTickChart.tsx`:

```typescript
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  Cell,
  ResponsiveContainer,
} from 'recharts';
import type { WorkerTickDto } from '@/shared/types/system';
import { format, parseISO } from 'date-fns';

interface Props {
  ticks: WorkerTickDto[];
}

interface ChartDatum {
  index: number;
  durationMs: number;
  isSuccess: boolean;
  startedAt: string;
  trigger: string;
  errorMessage: string | null;
}

interface TooltipPayload {
  payload: ChartDatum;
}

function CustomTooltip({
  active,
  payload,
}: {
  active?: boolean;
  payload?: TooltipPayload[];
}) {
  if (!active || !payload?.length) return null;
  const d = payload[0].payload;
  return (
    <div className="rounded border bg-background px-3 py-2 shadow text-xs space-y-0.5">
      <p className="font-medium">{d.isSuccess ? '✓ Success' : '✗ Failed'}</p>
      <p>Started: {format(parseISO(d.startedAt), 'MMM d HH:mm:ss')}</p>
      <p>Duration: {d.durationMs != null ? `${d.durationMs}ms` : '—'}</p>
      <p>Trigger: {d.trigger}</p>
      {d.errorMessage && (
        <p className="text-destructive max-w-[240px] break-words">
          Error: {d.errorMessage}
        </p>
      )}
    </div>
  );
}

export function WorkerTickChart({ ticks }: Props) {
  if (!ticks.length) {
    return (
      <p className="text-xs text-muted-foreground py-2">No tick history available.</p>
    );
  }

  // Show last 100 ticks in chronological order (oldest first = left)
  const data: ChartDatum[] = ticks
    .slice(-100)
    .map((t, i) => ({
      index: i,
      durationMs: t.durationMs ?? 0,
      isSuccess: t.isSuccess,
      startedAt: t.startedAt,
      trigger: t.trigger,
      errorMessage: t.errorMessage,
    }));

  return (
    <div className="h-[80px] w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} barCategoryGap={1} margin={{ top: 4, right: 0, left: 0, bottom: 0 }}>
          <XAxis dataKey="index" hide />
          <YAxis hide domain={[0, 'dataMax']} />
          <Tooltip content={<CustomTooltip />} />
          <Bar dataKey="durationMs" radius={[2, 2, 0, 0]}>
            {data.map((d, i) => (
              <Cell
                key={i}
                fill={d.isSuccess ? '#22c55e' : '#ef4444'}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
```

---

## Step 6 — Create WorkerCard component

- [ ] Create `src/MSOSync.Frontend/src/features/operations/health/components/WorkerCard.tsx`:

```typescript
import { useState } from 'react';
import { Card, CardHeader, CardContent } from '@/shared/components/ui/card';
import { Badge } from '@/shared/components/ui/badge';
import { Button } from '@/shared/components/ui/button';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { WorkerTickChart } from './WorkerTickChart';
import type { WorkerStatusDto, WorkerStateType } from '@/shared/types/system';
import { formatDistanceToNow, parseISO } from 'date-fns';

const STATE_COLORS: Record<WorkerStateType, string> = {
  Running:  'bg-blue-100 text-blue-800 border-blue-200',
  Idle:     'bg-gray-100 text-gray-600 border-gray-200',
  Warning:  'bg-yellow-100 text-yellow-800 border-yellow-200',
  Failed:   'bg-red-100 text-red-800 border-red-200',
  Delayed:  'bg-orange-100 text-orange-800 border-orange-200',
  Disabled: 'bg-gray-50 text-gray-400 border-gray-100',
};

function relative(iso: string | null): string {
  if (!iso) return '—';
  try {
    return formatDistanceToNow(parseISO(iso), { addSuffix: true });
  } catch {
    return iso;
  }
}

interface Props {
  worker: WorkerStatusDto;
}

export function WorkerCard({ worker }: Props) {
  const [expanded, setExpanded] = useState(false);

  return (
    <Card className="flex flex-col">
      <CardHeader className="pb-2 pt-4 px-4">
        <div className="flex items-start justify-between gap-2">
          <p className="text-sm font-semibold leading-tight truncate">{worker.workerName}</p>
          <Badge className={`shrink-0 border text-xs ${STATE_COLORS[worker.workerState] ?? STATE_COLORS['Idle']}`}>
            {worker.workerState}
          </Badge>
        </div>
        {worker.nextExpectedAt && (
          <p className="text-xs text-muted-foreground mt-0.5">
            Next: {relative(worker.nextExpectedAt)}
          </p>
        )}
      </CardHeader>

      <CardContent className="px-4 pb-3 flex-1 space-y-1">
        <div className="grid grid-cols-2 gap-x-4 gap-y-0.5 text-xs">
          <span className="text-muted-foreground">Last run</span>
          <span>{relative(worker.lastRunAt)}</span>

          <span className="text-muted-foreground">Avg duration</span>
          <span>{worker.avgDurationMs != null ? `${worker.avgDurationMs}ms` : '—'}</span>

          <span className="text-muted-foreground">Executions</span>
          <span>{worker.executionCount.toLocaleString()}</span>

          <span className="text-muted-foreground">Failures</span>
          <span>
            {worker.failureCount > 0 ? (
              <Badge className="h-4 px-1 text-xs bg-red-100 text-red-700 border-red-200">
                {worker.failureCount}
              </Badge>
            ) : (
              <span className="text-green-600">0</span>
            )}
          </span>
        </div>

        {/* Expand/collapse button */}
        <Button
          variant="ghost"
          size="sm"
          className="mt-2 h-6 w-full px-0 text-xs text-muted-foreground hover:text-foreground justify-start gap-1"
          onClick={() => setExpanded((v) => !v)}
        >
          {expanded ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
          {expanded ? 'Hide history' : 'Show history'}
        </Button>

        {expanded && (
          <div className="space-y-2 pt-1">
            <WorkerTickChart ticks={worker.recentTicks} />
            <div className="grid grid-cols-2 gap-x-4 gap-y-0.5 text-xs">
              <span className="text-muted-foreground">Success rate</span>
              <span>
                {worker.successRatePct != null
                  ? `${worker.successRatePct.toFixed(1)}%`
                  : '—'}
              </span>

              <span className="text-muted-foreground">Max duration</span>
              <span>{worker.maxDurationMs != null ? `${worker.maxDurationMs}ms` : '—'}</span>

              <span className="text-muted-foreground">Last failure</span>
              <span className="text-destructive">{relative(worker.lastFailureAt)}</span>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
```

---

## Step 7 — Create SystemHealthPanel component

- [ ] Create `src/MSOSync.Frontend/src/features/operations/health/components/SystemHealthPanel.tsx`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { fetchSystemHealth } from '@/shared/api/system';
import { systemKeys } from '@/shared/api/system';
import { Badge } from '@/shared/components/ui/badge';
import { Card, CardContent, CardHeader } from '@/shared/components/ui/card';
import type { HealthLevel } from '@/shared/types/system';

const LEVEL_COLORS: Record<HealthLevel, string> = {
  Healthy:  'bg-green-100 text-green-800 border-green-200',
  Degraded: 'bg-yellow-100 text-yellow-800 border-yellow-200',
  Critical: 'bg-red-100 text-red-800 border-red-200',
  Unknown:  'bg-gray-100 text-gray-600 border-gray-200',
};

export function SystemHealthPanel() {
  const { data, isLoading } = useQuery({
    queryKey: systemKeys.health,
    queryFn: fetchSystemHealth,
    staleTime: 15_000,
    refetchOnWindowFocus: true,
  });

  if (isLoading) {
    return <p className="text-xs text-muted-foreground">Loading health…</p>;
  }

  if (!data?.length) {
    return <p className="text-xs text-muted-foreground">No health data available.</p>;
  }

  return (
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {data.map((c) => (
        <Card key={c.contributor}>
          <CardHeader className="pb-1 pt-3 px-4">
            <div className="flex items-center justify-between">
              <p className="text-sm font-medium">{c.contributor}</p>
              <Badge className={`border text-xs ${LEVEL_COLORS[c.level] ?? LEVEL_COLORS['Unknown']}`}>
                {c.level}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="px-4 pb-3">
            {c.detail && (
              <p className="text-xs text-muted-foreground">{c.detail}</p>
            )}
            {c.latencyMs != null && (
              <p className="text-xs text-muted-foreground">Latency: {c.latencyMs}ms</p>
            )}
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
```

---

## Step 8 — Define worker sort order

The workers grid is sorted: Failed → Warning → Delayed → Running → Idle → Disabled.

- [ ] Create a sort helper at the top of `HealthPage.tsx` (see next step):

```typescript
const STATE_SORT_ORDER: Record<string, number> = {
  Failed:   0,
  Warning:  1,
  Delayed:  2,
  Running:  3,
  Idle:     4,
  Disabled: 5,
};

function sortWorkers(workers: WorkerStatusDto[]): WorkerStatusDto[] {
  return [...workers].sort((a, b) => {
    const ao = STATE_SORT_ORDER[a.workerState] ?? 99;
    const bo = STATE_SORT_ORDER[b.workerState] ?? 99;
    return ao - bo;
  });
}
```

---

## Step 9 — Create HealthPage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/operations/health/HealthPage.tsx`:

```typescript
import { useWorkers } from '@/shared/hooks/useWorkers';
import { WorkerCard } from './components/WorkerCard';
import { SystemHealthPanel } from './components/SystemHealthPanel';
import { Badge } from '@/shared/components/ui/badge';
import type { WorkerStatusDto, WorkerStateType } from '@/shared/types/system';
import { formatDistanceToNow, parseISO } from 'date-fns';

const STATE_SORT_ORDER: Record<string, number> = {
  Failed:   0,
  Warning:  1,
  Delayed:  2,
  Running:  3,
  Idle:     4,
  Disabled: 5,
};

function sortWorkers(workers: WorkerStatusDto[]): WorkerStatusDto[] {
  return [...workers].sort((a, b) => {
    const ao = STATE_SORT_ORDER[a.workerState] ?? 99;
    const bo = STATE_SORT_ORDER[b.workerState] ?? 99;
    return ao - bo;
  });
}

function findLongestRunning(workers: WorkerStatusDto[]): WorkerStatusDto | null {
  return (
    workers
      .filter((w) => w.workerState === 'Running' && w.lastRunAt != null)
      .sort((a, b) => new Date(a.lastRunAt!).getTime() - new Date(b.lastRunAt!).getTime())[0] ?? null
  );
}

const STATE_BADGE_COLORS: Record<WorkerStateType, string> = {
  Running:  'bg-blue-100 text-blue-800',
  Idle:     'bg-gray-100 text-gray-600',
  Warning:  'bg-yellow-100 text-yellow-800',
  Failed:   'bg-red-100 text-red-800',
  Delayed:  'bg-orange-100 text-orange-800',
  Disabled: 'bg-gray-50 text-gray-400',
};

const COUNTED_STATES: WorkerStateType[] = ['Running', 'Warning', 'Failed'];

export function HealthPage() {
  const { data: workers, isLoading } = useWorkers();

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center text-sm text-muted-foreground">
        Loading worker status…
      </div>
    );
  }

  const all = workers ?? [];
  const sorted = sortWorkers(all);
  const longest = findLongestRunning(all);

  const stateCounts = COUNTED_STATES.reduce<Partial<Record<WorkerStateType, number>>>(
    (acc, state) => {
      acc[state] = all.filter((w) => w.workerState === state).length;
      return acc;
    },
    {}
  );

  return (
    <div className="space-y-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Health</h1>
        <p className="text-sm text-muted-foreground">Worker status and system contributors</p>
      </div>

      {/* Workers Summary Bar */}
      <div className="flex flex-wrap items-center gap-4 rounded-lg border bg-card px-4 py-3">
        <span className="text-sm font-medium">{all.length} workers</span>

        {COUNTED_STATES.map((state) => (
          stateCounts[state]! > 0 && (
            <Badge key={state} className={`text-xs ${STATE_BADGE_COLORS[state]}`}>
              {stateCounts[state]} {state}
            </Badge>
          )
        ))}

        {longest && (
          <span className="ml-auto text-xs text-muted-foreground">
            Longest running:{' '}
            <span className="font-medium text-foreground">{longest.workerName}</span>
            {' — '}
            {formatDistanceToNow(parseISO(longest.lastRunAt!), { addSuffix: false })}
          </span>
        )}
      </div>

      {/* Workers Grid */}
      <section>
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-wider text-muted-foreground">
          Workers ({all.length})
        </h2>
        {sorted.length === 0 ? (
          <p className="text-sm text-muted-foreground">No workers registered.</p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {sorted.map((w) => (
              <WorkerCard key={w.workerId} worker={w} />
            ))}
          </div>
        )}
      </section>

      {/* System Health Section */}
      <section>
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-wider text-muted-foreground">
          System Health
        </h2>
        <SystemHealthPanel />
      </section>
    </div>
  );
}
```

---

## Step 10 — Wire WorkerStatusChanged in eventRouter.ts

- [ ] Open `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`. The `systemKeys` import should already exist from Task 13. Add a new case:

```typescript
case 'WorkerStatusChanged':
  queryClient.invalidateQueries({ queryKey: systemKeys.workers });
  break;
```

---

## Step 11 — Create barrel index

- [ ] Create `src/MSOSync.Frontend/src/features/operations/health/index.ts`:

```typescript
export { HealthPage } from './HealthPage';
```

---

## Step 12 — Build check

- [ ] Run:

```powershell
cd src/MSOSync.Frontend && npm run build 2>&1
```

Common issues to fix:
- Recharts `Cell` import: ensure `Cell` is imported from `recharts` (it is a named export)
- `date-fns` not installed: run `npm install date-fns` if Task 14 was skipped
- `WorkerStateType` is a union — ensure it is used as a string index type with `Record<WorkerStateType, ...>` only when all members are covered

---

## Step 13 — Manual smoke test

- [ ] Open `/operations/health`. Verify summary bar shows correct counts.
- [ ] Click "Show history" on a WorkerCard — verify the Recharts bar chart renders.
- [ ] Hover over a bar — verify the tooltip shows started time, duration, trigger, and error if present.
- [ ] Verify green bars for successful ticks, red bars for failed ticks.
- [ ] Verify System Health tiles appear at the bottom.

---

## Step 14 — Commit

- [ ] Stage files:

```powershell
git add src/MSOSync.Frontend/src/shared/hooks/useWorkers.ts
git add src/MSOSync.Frontend/src/features/operations/health/
git add src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts
```

- [ ] Commit:

```powershell
git commit -m "feat(12C-15): Health page with WorkerCard, tick chart, SystemHealthPanel, WorkerStatusChanged"
```
