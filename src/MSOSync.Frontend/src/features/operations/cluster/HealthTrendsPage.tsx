import { useState } from 'react';
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend } from 'recharts';
import { useHealthTrends } from '@/shared/hooks/useHealthTrends';
import type { NodeProbeStatsDto } from '@/shared/types/cluster';

const WINDOWS = ['1h', '6h', '24h', '7d'] as const;
type Window = typeof WINDOWS[number];

function ConnectivityBadge({ status }: { status: string }) {
  const color =
    status === 'Reachable'   ? 'bg-green-100 text-green-800' :
    status === 'Degraded'    ? 'bg-yellow-100 text-yellow-800' :
    status === 'Unreachable' ? 'bg-red-100 text-red-800' :
    'bg-gray-100 text-gray-700';
  return (
    <span className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${color}`}>
      {status}
    </span>
  );
}

export default function HealthTrendsPage() {
  const [window, setWindow]   = useState<Window>('6h');
  const [nodeId, setNodeId]   = useState<string | undefined>(undefined);
  const { data, isLoading, error } = useHealthTrends(window, nodeId);

  const chartData = data?.buckets.map(b => ({
    time:        new Date(b.bucketStart).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
    Reachable:   b.reachableCount,
    Degraded:    b.degradedCount,
    Unreachable: b.unreachableCount,
  })) ?? [];

  const nodeOptions = data?.nodeProbeStats.map(n => n.nodeId) ?? [];

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center gap-4">
        <h1 className="text-2xl font-semibold">Cluster Health Trends</h1>
        <div className="flex gap-1 ml-auto">
          {WINDOWS.map(w => (
            <button
              key={w}
              onClick={() => setWindow(w)}
              className={`px-3 py-1 rounded text-sm font-medium transition-colors ${
                window === w
                  ? 'bg-neutral-900 text-white dark:bg-white dark:text-neutral-900'
                  : 'bg-neutral-100 text-neutral-600 hover:bg-neutral-200 dark:bg-neutral-800 dark:text-neutral-400'
              }`}
            >
              {w}
            </button>
          ))}
        </div>
        {nodeOptions.length > 0 && (
          <select
            value={nodeId ?? ''}
            onChange={e => setNodeId(e.target.value || undefined)}
            className="text-sm border rounded px-2 py-1 bg-background"
            aria-label="Filter by node"
          >
            <option value="">All nodes</option>
            {nodeOptions.map(n => <option key={n} value={n}>{n}</option>)}
          </select>
        )}
      </div>

      {isLoading && <div className="text-sm text-muted-foreground">Loading health trends…</div>}
      {error && <div className="text-sm text-destructive">Failed to load health trends.</div>}

      {data && (
        <>
          <div className="rounded-lg border bg-card p-4">
            <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide mb-4">
              Connectivity Over Time
            </h2>
            <ResponsiveContainer width="100%" height={220}>
              <AreaChart data={chartData} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
                <XAxis dataKey="time" tick={{ fontSize: 11 }} />
                <YAxis allowDecimals={false} tick={{ fontSize: 11 }} />
                <Tooltip />
                <Legend />
                <Area type="monotone" dataKey="Reachable"   stackId="1" fill="#86efac" stroke="#22c55e" />
                <Area type="monotone" dataKey="Degraded"    stackId="1" fill="#fde68a" stroke="#f59e0b" />
                <Area type="monotone" dataKey="Unreachable" stackId="1" fill="#fca5a5" stroke="#ef4444" />
              </AreaChart>
            </ResponsiveContainer>
          </div>

          <div className="rounded-lg border bg-card p-4">
            <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide mb-3">
              Node Probe Stats
            </h2>
            {data.nodeProbeStats.length === 0 ? (
              <p className="text-sm text-muted-foreground">No connectivity data in this window.</p>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-muted-foreground border-b">
                    <th className="pb-2 font-medium">Node</th>
                    <th className="pb-2 font-medium">Status</th>
                    <th className="pb-2 font-medium">Consecutive Failures</th>
                    <th className="pb-2 font-medium">Uptime %</th>
                  </tr>
                </thead>
                <tbody>
                  {data.nodeProbeStats.map((n: NodeProbeStatsDto) => (
                    <tr key={n.nodeId} className="border-b last:border-0">
                      <td className="py-2 font-mono text-xs">{n.nodeId}</td>
                      <td className="py-2"><ConnectivityBadge status={n.connectivityStatus} /></td>
                      <td className="py-2 text-center">{n.consecutiveProbeFailures}</td>
                      <td className="py-2">{n.uptimePct.toFixed(1)}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}
    </div>
  );
}
