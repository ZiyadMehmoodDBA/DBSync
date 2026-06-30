import { Skeleton } from '../../components/ui/skeleton';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { useTopologySummary } from './hooks';

export function TopologySummaryCards() {
  const { data, isLoading, error, refetch } = useTopologySummary();

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />;

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
      <SummaryCard title="Total Groups" value={data?.totalGroups ?? '—'} />
      <SummaryCard title="Total Nodes" value={data?.totalNodes ?? '—'} />
      <SummaryCard title="Reachable" value={data?.reachableNodes ?? '—'} variant="success" />
      <SummaryCard title="Unreachable" value={data?.unreachableNodes ?? '—'} variant="danger" />
    </div>
  );
}
