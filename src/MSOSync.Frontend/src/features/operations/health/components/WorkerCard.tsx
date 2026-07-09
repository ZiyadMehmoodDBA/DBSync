import { useState } from 'react';
import { Card, CardHeader, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { WorkerTickChart } from './WorkerTickChart';
import type { WorkerStatusDto, WorkerStateType } from '@/shared/types/system';
import { formatRelativeTime } from '@/shared/utils/date';

const STATE_COLORS: Record<WorkerStateType, string> = {
  Running:  'bg-blue-100 text-blue-800 border-blue-200',
  Idle:     'bg-gray-100 text-gray-600 border-gray-200',
  Warning:  'bg-yellow-100 text-yellow-800 border-yellow-200',
  Failed:   'bg-red-100 text-red-800 border-red-200',
  Delayed:  'bg-orange-100 text-orange-800 border-orange-200',
  Disabled: 'bg-gray-50 text-gray-400 border-gray-100',
};

function relative(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    return formatRelativeTime(iso);
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
