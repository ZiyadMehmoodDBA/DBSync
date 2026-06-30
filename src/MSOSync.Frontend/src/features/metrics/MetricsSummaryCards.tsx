import { Skeleton } from '../../components/ui/skeleton';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { formatQueueDepth, formatPercent } from '../../shared/utils/numbers';
import { formatRelativeTime } from '../../shared/utils/date';
import { useMetricsSummary } from './hooks';

export function MetricsSummaryCards() {
  const { data, isLoading, error, refetch } = useMetricsSummary();

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
        <SummaryCard title="Queue In" value={data ? formatQueueDepth(data.incomingQueueDepth) : '—'} />
        <SummaryCard title="Queue Out" value={data ? formatQueueDepth(data.outgoingQueueDepth) : '—'} />
        <SummaryCard title="Batches 24h" value={data?.batchesProcessed24h ?? '—'} variant="success" />
        <SummaryCard title="Errors 24h" value={data?.errors24h ?? '—'} variant={data && data.errors24h > 0 ? 'danger' : 'default'} />
        <SummaryCard title="Error Rate" value={data ? formatPercent(data.errorRatePercent) : '—'} variant={data && data.errorRatePercent > 5 ? 'warning' : 'default'} />
        <SummaryCard title="Throughput/min" value={data?.throughputPerMinute ?? '—'} />
      </div>
      {data?.generatedAt && (
        <p className="text-xs text-neutral-500 dark:text-neutral-400">
          Updated {formatRelativeTime(data.generatedAt)}
        </p>
      )}
    </div>
  );
}
