import { useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { useNodeLifecycleHistory } from '../../../../shared/hooks/useNodeLifecycle';
import type { LifecycleHistoryDto, LifecycleTrigger } from '../../../../shared/types/lifecycle';
import { LifecycleBadge } from '../../../../shared/components/node';
import { Button } from '../../../../components/ui/button';

const TRIGGERS: LifecycleTrigger[] = [
  'Manual', 'Registration', 'Activation', 'Recovery', 'System', 'Timeout', 'Migration',
];

export function TimelineEntry({ entry }: { entry: LifecycleHistoryDto }) {
  const [expanded, setExpanded] = useState(false);
  return (
    <li className="border-l-2 border-neutral-200 py-2 pl-4 dark:border-neutral-700">
      <div className="flex flex-wrap items-center gap-2 text-sm">
        {entry.fromState === null ? (
          <span className="italic text-neutral-500">entered lifecycle model</span>
        ) : (
          <>
            <LifecycleBadge state={entry.fromState} />
            <span aria-hidden>→</span>
          </>
        )}
        <LifecycleBadge state={entry.toState} />
        <span className="text-neutral-500">{entry.trigger}</span>
        <span className="text-neutral-500">by {entry.actor}</span>
        <span className="ml-auto text-xs text-neutral-400">
          {new Date(entry.occurredAt).toLocaleString()}
        </span>
      </div>
      {entry.reason && <p className="mt-1 text-xs text-neutral-500">{entry.reason}</p>}
      {entry.correlationId && (
        <div className="mt-1">
          <button
            type="button"
            className="inline-flex items-center gap-1 text-xs text-neutral-400 hover:text-neutral-600"
            onClick={() => setExpanded(!expanded)}
          >
            {expanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
            details
          </button>
          <span className={expanded ? 'ml-2 font-mono text-xs' : 'hidden ml-2 font-mono text-xs'} hidden={!expanded}>
            {entry.correlationId}
          </span>
        </div>
      )}
    </li>
  );
}

export function LifecycleHistoryTimeline({ nodeId }: { nodeId: string }) {
  const [page, setPage] = useState(1);
  const [trigger, setTrigger] = useState<LifecycleTrigger | ''>('');
  const { data, isLoading } = useNodeLifecycleHistory(nodeId, {
    page, pageSize: 25, trigger: trigger || undefined,
  });

  // Group by day
  const groups = new Map<string, LifecycleHistoryDto[]>();
  for (const item of data?.items ?? []) {
    const day = new Date(item.occurredAt).toLocaleDateString();
    groups.set(day, [...(groups.get(day) ?? []), item]);
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <select
          className="rounded border p-1 text-sm"
          value={trigger}
          onChange={(e) => { setTrigger(e.target.value as LifecycleTrigger | ''); setPage(1); }}
        >
          <option value="">All triggers</option>
          {TRIGGERS.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
      </div>
      {isLoading && <p className="text-sm text-neutral-500">Loading timeline…</p>}
      {[...groups.entries()].map(([day, items]) => (
        <div key={day}>
          <h4 className="mb-1 text-xs font-semibold uppercase text-neutral-500">{day}</h4>
          <ul>{items.map((e) => <TimelineEntry key={e.historyId} entry={e} />)}</ul>
        </div>
      ))}
      {data && data.totalCount > data.pageSize && (
        <div className="flex gap-2">
          <Button size="sm" variant="outline" disabled={page === 1} onClick={() => setPage(page - 1)}>
            Previous
          </Button>
          <Button
            size="sm" variant="outline"
            disabled={page * data.pageSize >= data.totalCount}
            onClick={() => setPage(page + 1)}
          >
            Next
          </Button>
        </div>
      )}
    </div>
  );
}
