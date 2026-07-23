import { useState } from 'react';
import { useClusterDiagnostics } from '@/shared/hooks/useClusterDiagnostics';
import type { RuntimeStatsDto, ActiveLockDto, SlowOperationDto } from '@/shared/types/cluster';

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(true);
  return (
    <div className="rounded-lg border bg-card">
      <button
        onClick={() => setOpen(!open)}
        className="w-full flex items-center justify-between p-4 text-left"
      >
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">{title}</h2>
        <span className="text-xs text-muted-foreground">{open ? '▲' : '▼'}</span>
      </button>
      {open && <div className="px-4 pb-4">{children}</div>}
    </div>
  );
}

function ProgressBar({ used, max }: { used: number | null; max: number | null }) {
  if (!used || !max || max === 0) return null;
  const pct = Math.min((used / max) * 100, 100);
  const color = pct > 90 ? 'bg-red-500' : pct > 70 ? 'bg-yellow-500' : 'bg-green-500';
  return (
    <div className="w-full h-1.5 bg-muted rounded-full overflow-hidden">
      <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

export default function ClusterDiagnosticsPage() {
  const { data, isLoading, error } = useClusterDiagnostics();

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Loading diagnostics…</div>;
  if (error || !data) return <div className="p-6 text-sm text-destructive">Failed to load diagnostics.</div>;

  const { runtimeStats, activeLocks, slowOperations } = data;
  const latest = runtimeStats[0];

  return (
    <div className="p-6 space-y-4">
      <h1 className="text-2xl font-semibold">Cluster Diagnostics</h1>

      {/* Summary cards from latest stats */}
      {latest && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">Heap Used</p>
            <p className="font-semibold">{latest.heapUsedMb !== null ? `${latest.heapUsedMb.toFixed(1)} MB` : '—'}</p>
            <ProgressBar used={latest.heapUsedMb} max={latest.heapMaxMb} />
          </div>
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">CPU</p>
            <p className="font-semibold">{latest.cpuPercent !== null ? `${latest.cpuPercent.toFixed(1)}%` : '—'}</p>
          </div>
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">Threads</p>
            <p className="font-semibold">{latest.threadCount ?? '—'}</p>
          </div>
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">Uptime (h)</p>
            <p className="font-semibold">{latest.uptimeHours !== null ? latest.uptimeHours.toFixed(2) : '—'}</p>
          </div>
        </div>
      )}

      <Panel title={`Runtime Stats (last ${runtimeStats.length})`}>
        {runtimeStats.length === 0 ? (
          <p className="text-sm text-muted-foreground">No runtime stats available.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead>
                <tr className="text-left text-muted-foreground border-b">
                  <th className="pb-2 font-medium">Captured</th>
                  <th className="pb-2 font-medium">Heap (MB)</th>
                  <th className="pb-2 font-medium">CPU %</th>
                  <th className="pb-2 font-medium">Threads</th>
                  <th className="pb-2 font-medium">GC Count</th>
                  <th className="pb-2 font-medium">Uptime (h)</th>
                </tr>
              </thead>
              <tbody>
                {runtimeStats.map((s: RuntimeStatsDto) => (
                  <tr key={s.statId} className="border-b last:border-0">
                    <td className="py-1.5">{new Date(s.capturedAt).toLocaleTimeString()}</td>
                    <td className="py-1.5">{s.heapUsedMb !== null ? `${s.heapUsedMb.toFixed(1)} / ${s.heapMaxMb?.toFixed(1) ?? '?'}` : '—'}</td>
                    <td className="py-1.5">{s.cpuPercent !== null ? `${s.cpuPercent.toFixed(1)}%` : '—'}</td>
                    <td className="py-1.5">{s.threadCount ?? '—'}</td>
                    <td className="py-1.5">{s.gcCount ?? '—'}</td>
                    <td className="py-1.5">{s.uptimeHours !== null ? s.uptimeHours.toFixed(2) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Panel title={`Active Locks (${activeLocks.length})`}>
        {activeLocks.length === 0 ? (
          <p className="text-sm text-muted-foreground">No active locks.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Lock Name</th>
                <th className="pb-2 font-medium">Owner</th>
                <th className="pb-2 font-medium">Age (s)</th>
                <th className="pb-2 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {activeLocks.map((l: ActiveLockDto) => (
                <tr key={l.lockName} className={`border-b last:border-0 ${l.isStale ? 'bg-red-50 dark:bg-red-950/20' : ''}`}>
                  <td className="py-2 font-mono text-xs">{l.lockName}</td>
                  <td className="py-2 text-xs">{l.lockOwner}</td>
                  <td className="py-2">{l.ageSeconds.toFixed(0)}</td>
                  <td className="py-2">
                    {l.isStale
                      ? <span className="inline-flex rounded px-2 py-0.5 text-xs font-medium bg-red-100 text-red-800">Stale</span>
                      : <span className="inline-flex rounded px-2 py-0.5 text-xs font-medium bg-green-100 text-green-800">Active</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>

      <Panel title={`Slow Operations (${slowOperations.length})`}>
        {slowOperations.length === 0 ? (
          <p className="text-sm text-muted-foreground">No running or pending operations.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Type</th>
                <th className="pb-2 font-medium">Status</th>
                <th className="pb-2 font-medium">Duration (min)</th>
                <th className="pb-2 font-medium">Progress</th>
              </tr>
            </thead>
            <tbody>
              {slowOperations.map((op: SlowOperationDto) => (
                <tr key={op.operationId} className="border-b last:border-0">
                  <td className="py-2">{op.operationType}</td>
                  <td className="py-2">
                    <span className={`inline-flex rounded px-2 py-0.5 text-xs font-medium ${
                      op.status === 'Running' ? 'bg-blue-100 text-blue-800' : 'bg-yellow-100 text-yellow-800'
                    }`}>{op.status}</span>
                  </td>
                  <td className="py-2 font-semibold">{op.durationMinutes.toFixed(1)}</td>
                  <td className="py-2">
                    {op.progressPercent !== null ? (
                      <div className="flex items-center gap-2">
                        <div className="w-20 h-1.5 bg-muted rounded-full overflow-hidden">
                          <div className="h-full bg-blue-500 rounded-full" style={{ width: `${op.progressPercent}%` }} />
                        </div>
                        <span className="text-xs">{op.progressPercent}%</span>
                      </div>
                    ) : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
    </div>
  );
}
