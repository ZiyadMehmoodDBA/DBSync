import { Skeleton } from '../../components/ui/skeleton';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { formatRelativeTime } from '../../shared/utils/date';
import { formatQueueDepth } from '../../shared/utils/numbers';
import { useDashboardSummary } from './hooks';

export function SummaryCards() {
  const { data, error, isLoading, refetch } = useDashboardSummary();

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />;

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        {Array.from({ length: 6 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        <SummaryCard title="Total Nodes" value={data?.totalNodes ?? '—'} />
        <SummaryCard title="Reachable" value={data?.reachableNodes ?? '—'} variant="success" />
        <SummaryCard title="Degraded" value={data?.degradedNodes ?? '—'} variant="warning" />
        <SummaryCard title="Unreachable" value={data?.unreachableNodes ?? '—'} variant="danger" />
        <SummaryCard title="Events Today" value={data?.eventsToday ?? '—'} />
        <SummaryCard
          title="Queue Depth"
          value={data ? formatQueueDepth(data.queueDepth) : '—'}
        />
      </div>
      {data?.generatedAt && (
        <p className="text-xs text-neutral-500 dark:text-neutral-400">
          Updated {formatRelativeTime(data.generatedAt)}
        </p>
      )}
    </div>
  );
}
