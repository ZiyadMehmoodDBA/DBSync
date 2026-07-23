import { useRecoveryDashboard } from '@/shared/hooks/useRecoveryDashboard';
import type { ActiveRecoveryDto, CompletedRecoveryDto } from '@/shared/types/cluster';

function SummaryCard({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border bg-card p-4 space-y-1">
      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">{label}</p>
      <p className="text-3xl font-bold">{value}</p>
    </div>
  );
}

function StatusChip({ status }: { status: string }) {
  const color =
    status === 'Running'   ? 'bg-blue-100 text-blue-800' :
    status === 'Completed' ? 'bg-green-100 text-green-800' :
    status === 'Failed'    ? 'bg-red-100 text-red-800' :
    'bg-gray-100 text-gray-700';
  return <span className={`inline-flex rounded px-2 py-0.5 text-xs font-medium ${color}`}>{status}</span>;
}

export default function RecoveryDashboardPage() {
  const { data, isLoading, error } = useRecoveryDashboard();

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Loading recovery dashboard…</div>;
  if (error || !data) return <div className="p-6 text-sm text-destructive">Failed to load recovery dashboard.</div>;

  const { summary, activeRecoveries, recentCompletedRecoveries } = data;

  return (
    <div className="p-6 space-y-6">
      <h1 className="text-2xl font-semibold">Disaster Recovery Dashboard</h1>

      {/* Summary cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <SummaryCard label="Active Recoveries"     value={summary.activeCount} />
        <SummaryCard label="Avg RTO (min)"         value={summary.avgRtoMinutes !== null ? summary.avgRtoMinutes.toFixed(1) : '—'} />
        <SummaryCard label="Max RTO (min)"         value={summary.maxRtoMinutes !== null ? summary.maxRtoMinutes.toFixed(1) : '—'} />
        <SummaryCard label="Completed (30d)"       value={summary.completedLast30Days} />
      </div>

      {/* Active recoveries */}
      <div className="rounded-lg border bg-card p-4 space-y-3">
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
          Active Recoveries ({activeRecoveries.length})
        </h2>
        {activeRecoveries.length === 0 ? (
          <p className="text-sm text-muted-foreground">No nodes currently in recovery.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Node</th>
                <th className="pb-2 font-medium">Recovery Started</th>
                <th className="pb-2 font-medium">Elapsed (min)</th>
                <th className="pb-2 font-medium">Replay Ops</th>
              </tr>
            </thead>
            <tbody>
              {activeRecoveries.map((r: ActiveRecoveryDto) => (
                <tr key={r.nodeId} className="border-b last:border-0">
                  <td className="py-2 font-mono text-xs">{r.nodeId}</td>
                  <td className="py-2 text-xs text-muted-foreground">
                    {new Date(r.recoveryStartedAt).toLocaleString()}
                  </td>
                  <td className="py-2">{r.elapsedMinutes.toFixed(1)}</td>
                  <td className="py-2">
                    {r.associatedReplayOps.length === 0 ? (
                      <span className="text-muted-foreground text-xs">none</span>
                    ) : (
                      <div className="flex gap-1 flex-wrap">
                        {r.associatedReplayOps.map(op => (
                          <StatusChip key={op.operationId} status={op.status} />
                        ))}
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Completed recoveries */}
      <div className="rounded-lg border bg-card p-4 space-y-3">
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
          Recent Completed Recoveries (last 30 days)
        </h2>
        {recentCompletedRecoveries.length === 0 ? (
          <p className="text-sm text-muted-foreground">No completed recoveries in the last 30 days.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Node</th>
                <th className="pb-2 font-medium">Recovery Started</th>
                <th className="pb-2 font-medium">Restored At</th>
                <th className="pb-2 font-medium">RTO (min)</th>
              </tr>
            </thead>
            <tbody>
              {recentCompletedRecoveries.map((r: CompletedRecoveryDto) => (
                <tr key={`${r.nodeId}-${r.restoredAt}`} className="border-b last:border-0">
                  <td className="py-2 font-mono text-xs">{r.nodeId}</td>
                  <td className="py-2 text-xs text-muted-foreground">
                    {new Date(r.recoveryStartedAt).toLocaleString()}
                  </td>
                  <td className="py-2 text-xs text-muted-foreground">
                    {new Date(r.restoredAt).toLocaleString()}
                  </td>
                  <td className="py-2 font-semibold">{r.rtoMinutes.toFixed(1)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
