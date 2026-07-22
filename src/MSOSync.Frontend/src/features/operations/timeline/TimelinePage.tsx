import { useState, useMemo } from 'react';
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, Cell,
} from 'recharts';
import type { TooltipContentProps } from 'recharts/types/component/Tooltip';
import type { ValueType, NameType } from 'recharts/types/component/DefaultTooltipContent';
import { useOperationTimeline } from '@/shared/hooks/useOperationTimeline';
import { Button } from '@/components/ui/button';
import { AlertTriangle } from 'lucide-react';
import { subHours, subDays, format, parseISO } from 'date-fns';

const ALL_TYPES = [
  'Export', 'Rollout', 'Decommission', 'Recovery',
  'RollingMaintenance', 'RollingUpgrade', 'BatchReplay',
];

const STATUS_COLOR: Record<string, string> = {
  Running:   '#3b82f6',
  Completed: '#22c55e',
  Failed:    '#ef4444',
  Cancelled: '#9ca3af',
  Pending:   '#f59e0b',
};

function toIso(d: Date): string {
  return d.toISOString();
}

function defaultRange() {
  const to   = new Date();
  const from = subHours(to, 24);
  return { from: toIso(from), to: toIso(to) };
}

interface GanttDatum {
  name:        string;
  operationId: string;
  start:       number;   // epoch ms
  duration:    number;   // ms
  status:      string;
  label:       string;
  startMs:     number;
  endMs:       number;
}

export default function TimelinePage() {
  const [range, setRange]         = useState(defaultRange);
  const [selectedTypes, setTypes] = useState<string[]>([]);

  const { data, isFetching } = useOperationTimeline(range.from, range.to, selectedTypes);

  const nowMs = Date.now();

  const ganttData: GanttDatum[] = useMemo(() => {
    if (!data) return [];
    return data.items.map(item => {
      const startMs = parseISO(item.startedAt).getTime();
      const endMs   = item.completedAt ? parseISO(item.completedAt).getTime() : nowMs;
      return {
        name:        item.type,
        operationId: item.operationId,
        start:       startMs,
        duration:    Math.max(endMs - startMs, 60_000), // min 1 min for visibility
        status:      item.status,
        label:       item.label ?? item.type,
        startMs,
        endMs,
      };
    });
  }, [data, nowMs]);

  const domainMin = ganttData.length > 0 ? Math.min(...ganttData.map(d => d.startMs)) : Date.now() - 86_400_000;
  const domainMax = nowMs;

  const toggleType = (t: string) =>
    setTypes(prev => prev.includes(t) ? prev.filter(x => x !== t) : [...prev, t]);

  return (
    <div className="p-6 space-y-4">
      <h1 className="text-2xl font-semibold">Operations Timeline</h1>

      {/* Controls */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <label className="text-xs text-muted-foreground">From (UTC)</label>
          <input
            type="datetime-local"
            className="rounded border bg-background px-2 py-1 text-sm"
            value={range.from.slice(0, 16)}
            onChange={e => setRange(r => ({ ...r, from: new Date(e.target.value).toISOString() }))}
          />
        </div>
        <div className="flex items-center gap-2">
          <label className="text-xs text-muted-foreground">To (UTC)</label>
          <input
            type="datetime-local"
            className="rounded border bg-background px-2 py-1 text-sm"
            value={range.to.slice(0, 16)}
            onChange={e => setRange(r => ({ ...r, to: new Date(e.target.value).toISOString() }))}
          />
        </div>

        {/* Quick range buttons */}
        <div className="flex gap-1">
          {[
            { label: '1h',  fn: () => ({ from: toIso(subHours(new Date(), 1)),  to: toIso(new Date()) }) },
            { label: '24h', fn: () => ({ from: toIso(subHours(new Date(), 24)), to: toIso(new Date()) }) },
            { label: '7d',  fn: () => ({ from: toIso(subDays(new Date(), 7)),   to: toIso(new Date()) }) },
          ].map(({ label, fn }) => (
            <Button key={label} variant="outline" size="sm" onClick={() => setRange(fn())}>
              {label}
            </Button>
          ))}
        </div>

        {/* Type filter chips */}
        <div className="flex flex-wrap gap-1">
          {ALL_TYPES.map(t => (
            <button
              key={t}
              className={`rounded-full px-2 py-0.5 text-xs font-medium border transition-colors ${
                selectedTypes.includes(t) || selectedTypes.length === 0
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'border-border text-muted-foreground hover:border-primary'
              }`}
              onClick={() => toggleType(t)}
            >
              {t}
            </button>
          ))}
          {selectedTypes.length > 0 && (
            <button className="text-xs text-muted-foreground underline" onClick={() => setTypes([])}>
              Clear
            </button>
          )}
        </div>
      </div>

      {/* HasMore warning */}
      {data?.hasMore && (
        <div className="flex items-center gap-2 rounded border border-amber-300 bg-amber-50 dark:bg-amber-950/20 px-3 py-2 text-sm text-amber-700 dark:text-amber-400">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          Showing {data.returnedCount} of more operations — narrow the time range or add type filters to see all.
        </div>
      )}

      {/* Chart */}
      {isFetching ? (
        <div className="h-64 flex items-center justify-center text-sm text-muted-foreground">
          Loading timeline…
        </div>
      ) : ganttData.length === 0 ? (
        <div className="h-64 flex items-center justify-center rounded-lg border bg-card text-sm text-muted-foreground">
          No operations in this range.
        </div>
      ) : (
        <div className="rounded-lg border bg-card p-4">
          <ResponsiveContainer width="100%" height={Math.max(ganttData.length * 36, 200)}>
            <BarChart
              layout="vertical"
              data={ganttData}
              margin={{ top: 8, right: 24, bottom: 8, left: 120 }}
            >
              <XAxis
                type="number"
                domain={[domainMin, domainMax]}
                tickFormatter={(v: number) => format(new Date(v), 'HH:mm')}
                scale="linear"
              />
              <YAxis
                type="category"
                dataKey="name"
                width={110}
                tick={{ fontSize: 11 }}
              />
              <Tooltip
                content={(props: TooltipContentProps<ValueType, NameType>) => {
                  const { active, payload } = props;
                  if (!active || !payload?.[0]) return null;
                  const d = (payload[0] as { payload: GanttDatum }).payload;
                  const durationMs = d.endMs - d.startMs;
                  const mins = Math.round(durationMs / 60_000);
                  return (
                    <div className="rounded border bg-background shadow-md px-3 py-2 text-xs space-y-1">
                      <p className="font-semibold">{d.label}</p>
                      <p className="text-muted-foreground">{d.status}</p>
                      <p>{format(new Date(d.startMs), 'HH:mm:ss')} UTC</p>
                      <p>{mins < 60 ? `${mins}m` : `${Math.floor(mins / 60)}h ${mins % 60}m`}</p>
                    </div>
                  );
                }}
              />
              {/* Invisible bar from 0 to start for offset */}
              <Bar dataKey="start" stackId="g" fill="transparent" />
              {/* Visible bar from start to end */}
              <Bar dataKey="duration" stackId="g" radius={[0, 3, 3, 0]}>
                {ganttData.map((entry, i) => (
                  <Cell
                    key={i}
                    fill={STATUS_COLOR[entry.status] ?? '#9ca3af'}
                  />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>

          {/* Legend */}
          <div className="flex flex-wrap gap-3 mt-3 px-1">
            {Object.entries(STATUS_COLOR).map(([status, color]) => (
              <div key={status} className="flex items-center gap-1.5 text-xs">
                <span className="h-2.5 w-2.5 rounded-sm" style={{ backgroundColor: color }} />
                {status}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
