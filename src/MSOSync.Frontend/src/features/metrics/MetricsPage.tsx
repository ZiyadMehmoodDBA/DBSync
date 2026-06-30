import { MetricsSummaryCards } from './MetricsSummaryCards';
import { NodeMetricsGrid } from './NodeMetricsGrid';
import { ChannelMetricsGrid } from './ChannelMetricsGrid';
import { useRuntimeMetrics } from './hooks';
import { formatUptime, formatPercent } from '../../shared/utils/numbers';

function RuntimeRow() {
  const { data, isLoading } = useRuntimeMetrics();
  if (isLoading || !data) return null;
  return (
    <div className="flex flex-wrap gap-6 rounded-lg border border-neutral-200 dark:border-neutral-800 p-4 text-sm">
      <span><span className="font-medium">Uptime:</span> {formatUptime(data.uptimeSeconds)}</span>
      <span><span className="font-medium">Memory:</span> {data.memoryMb} MB</span>
      <span><span className="font-medium">CPU:</span> {formatPercent(data.cpuPercent)}</span>
      <span><span className="font-medium">Workers:</span> {data.activeWorkers}</span>
    </div>
  );
}

export function MetricsPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <h1 className="text-2xl font-semibold">Metrics</h1>
      <MetricsSummaryCards />
      <RuntimeRow />
      <div>
        <h2 className="text-base font-semibold mb-3">Node Metrics</h2>
        <NodeMetricsGrid />
      </div>
      <div>
        <h2 className="text-base font-semibold mb-3">Channel Metrics</h2>
        <ChannelMetricsGrid />
      </div>
    </div>
  );
}
