import { useWorkers } from '@/shared/hooks/useWorkers';
import { WorkerCard } from './components/WorkerCard';
import { SystemHealthPanel } from './components/SystemHealthPanel';
import { Badge } from '@/components/ui/badge';
import type { WorkerStatusDto, WorkerStateType } from '@/shared/types/system';
import { formatRelativeTime } from '@/shared/utils/date';

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
    const ao = STATE_SORT_ORDER[a.state] ?? 99;
    const bo = STATE_SORT_ORDER[b.state] ?? 99;
    return ao - bo;
  });
}

function findLongestRunning(workers: WorkerStatusDto[]): WorkerStatusDto | null {
  return (
    workers
      .filter((w) => w.state === 'Running' && w.lastStarted != null)
      .sort((a, b) => new Date(a.lastStarted!).getTime() - new Date(b.lastStarted!).getTime())[0] ?? null
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
        Loading worker status...
      </div>
    );
  }

  const all = workers ?? [];
  const sorted = sortWorkers(all);
  const longest = findLongestRunning(all);

  const stateCounts = COUNTED_STATES.reduce<Partial<Record<WorkerStateType, number>>>(
    (acc, state) => {
      acc[state] = all.filter((w) => w.state === state).length;
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

        {COUNTED_STATES.map((state) =>
          (stateCounts[state] ?? 0) > 0 ? (
            <Badge key={state} className={`text-xs ${STATE_BADGE_COLORS[state]}`}>
              {stateCounts[state]} {state}
            </Badge>
          ) : null
        )}

        {longest && (
          <span className="ml-auto text-xs text-muted-foreground">
            Longest running:{' '}
            <span className="font-medium text-foreground">{longest.workerName}</span>
            {' — '}
            {formatRelativeTime(longest.lastStarted!)}
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
              <WorkerCard key={w.workerName} worker={w} />
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
