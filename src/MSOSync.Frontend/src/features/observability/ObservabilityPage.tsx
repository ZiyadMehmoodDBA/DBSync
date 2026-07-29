import { useHealthScores, useSloStatus } from './hooks';
import { NodeHealthTable } from './components/NodeHealthTable';
import { SloStatusCard } from './components/SloStatusCard';

export function ObservabilityPage() {
  const { data: healthScores, isLoading: scoresLoading, error: scoresError } = useHealthScores();
  const { data: sloStatus, isLoading: sloLoading } = useSloStatus();

  return (
    <div className="space-y-6 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Observability</h1>
        <span className="text-sm text-muted-foreground">Auto-refreshes every 30s</span>
      </div>

      <section>
        <h2 className="text-lg font-medium mb-3">SLO Status</h2>
        {sloLoading ? (
          <div className="text-muted-foreground">Loading SLO status…</div>
        ) : sloStatus ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <SloStatusCard
              label="Delivery Rate"
              value={`${(sloStatus.deliveryRate * 100).toFixed(3)}%`}
              target={`≥ ${(sloStatus.deliveryRateTarget * 100).toFixed(1)}%`}
              met={sloStatus.deliveryRateMet}
            />
            <SloStatusCard
              label="P99 Latency"
              value={`${sloStatus.latencyP99Ms.toFixed(0)}ms`}
              target={`≤ ${sloStatus.latencyP99TargetMs}ms`}
              met={sloStatus.latencyP99Met}
            />
          </div>
        ) : (
          <div className="text-muted-foreground">No SLO data available</div>
        )}
      </section>

      <section>
        <h2 className="text-lg font-medium mb-3">Node Health Scores</h2>
        {scoresError ? (
          <div className="text-red-500">Failed to load health scores</div>
        ) : (
          <NodeHealthTable scores={healthScores ?? []} loading={scoresLoading} />
        )}
      </section>
    </div>
  );
}
