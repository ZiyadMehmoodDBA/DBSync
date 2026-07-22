import { useClusterSummary } from '@/shared/hooks/useClusterSummary';
import { formatDistanceToNow } from 'date-fns';

function StatusBadge({ status }: { status: string }) {
  const color =
    status === 'Running'   ? 'bg-blue-100 text-blue-800' :
    status === 'Pending'   ? 'bg-yellow-100 text-yellow-800' :
    status === 'Completed' ? 'bg-green-100 text-green-800' :
    status === 'Failed'    ? 'bg-red-100 text-red-800' :
    'bg-gray-100 text-gray-700';
  return (
    <span className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${color}`}>
      {status}
    </span>
  );
}

export default function ClusterPage() {
  const { data, isLoading, error } = useClusterSummary();

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Loading cluster summary…</div>;
  if (error || !data) return <div className="p-6 text-sm text-destructive">Failed to load cluster summary.</div>;

  const { nodeStates, operationCounts, activeOperations, activeRollingOps, activeReplays, recentNodeChanges } = data;

  return (
    <div className="p-6 space-y-6">
      <h1 className="text-2xl font-semibold">Cluster Operations</h1>

      {/* 2×2 grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">

        {/* Node States */}
        <div className="rounded-lg border bg-card p-4 space-y-3">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">Node States</h2>
          <div className="flex flex-wrap gap-2">
            {[
              { label: 'Active',      count: nodeStates.active,      color: 'bg-green-100 text-green-800' },
              { label: 'Maintenance', count: nodeStates.maintenance,  color: 'bg-yellow-100 text-yellow-800' },
              { label: 'Draining',    count: nodeStates.draining,     color: 'bg-orange-100 text-orange-800' },
              { label: 'Offline',     count: nodeStates.offline,      color: 'bg-gray-100 text-gray-700' },
            ].map(({ label, count, color }) => (
              <div key={label} className={`flex items-center gap-1.5 rounded px-3 py-1.5 ${color}`}>
                <span className="text-xl font-bold">{count}</span>
                <span className="text-xs font-medium">{label}</span>
              </div>
            ))}
          </div>
          <p className="text-xs text-muted-foreground">{nodeStates.total} total nodes</p>
        </div>

        {/* Active Operations */}
        <div className="rounded-lg border bg-card p-4 space-y-2">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
            Active Operations
            {activeOperations.length > 0 && (
              <span className="ml-2 text-foreground">({operationCounts.running} running, {operationCounts.pending} pending)</span>
            )}
          </h2>
          {activeOperations.length === 0 ? (
            <p className="text-sm text-muted-foreground">No active operations</p>
          ) : (
            <div className="space-y-2 max-h-48 overflow-y-auto">
              {activeOperations.map(op => (
                <div key={op.operationId} className="flex items-center justify-between text-sm">
                  <div className="flex items-center gap-2">
                    <StatusBadge status={op.status} />
                    <span className="font-medium">{op.type}</span>
                    {op.nodeId && <span className="text-muted-foreground text-xs">{op.nodeId}</span>}
                  </div>
                  <span className="text-xs text-muted-foreground">
                    {formatDistanceToNow(new Date(op.startedAt), { addSuffix: true })}
                  </span>
                </div>
              ))}
            </div>
          )}
          <div className="flex gap-3 text-xs text-muted-foreground pt-1 border-t">
            <span>&#10003; {operationCounts.succeededToday} today</span>
            <span>&#10007; {operationCounts.failedToday} failed</span>
          </div>
        </div>

        {/* Rolling Operations */}
        <div className="rounded-lg border bg-card p-4 space-y-2">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">Rolling Operations</h2>
          {activeRollingOps.length === 0 ? (
            <p className="text-sm text-muted-foreground">No active rolling operations</p>
          ) : (
            <div className="space-y-3">
              {activeRollingOps.map(op => (
                <div key={op.operationId} className="space-y-1">
                  <div className="flex items-center justify-between text-sm">
                    <div className="flex items-center gap-2">
                      <StatusBadge status={op.status} />
                      <span className="font-medium">{op.mode === 'RollingMaintenance' ? 'Maintenance' : 'Upgrade'}</span>
                    </div>
                    <span className="text-xs text-muted-foreground">
                      Wave {op.currentWave}/{op.totalWaves} &middot; {op.nodesDone}/{op.nodesTotal} nodes
                      {op.nodesFailed > 0 && <span className="text-red-600 ml-1">({op.nodesFailed} failed)</span>}
                    </span>
                  </div>
                  <div className="w-full h-1.5 bg-muted rounded-full overflow-hidden">
                    <div
                      className="h-full bg-blue-500 rounded-full transition-all"
                      style={{ width: op.nodesTotal > 0 ? `${(op.nodesDone / op.nodesTotal) * 100}%` : '0%' }}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Replay Operations */}
        <div className="rounded-lg border bg-card p-4 space-y-2">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">Replay Operations</h2>
          {activeReplays.length === 0 ? (
            <p className="text-sm text-muted-foreground">No active replay operations</p>
          ) : (
            <div className="space-y-3">
              {activeReplays.map(op => (
                <div key={op.operationId} className="space-y-1">
                  <div className="flex items-center justify-between text-sm">
                    <div className="flex items-center gap-2">
                      <StatusBadge status={op.status} />
                      <span className="font-medium">{op.replayMode}</span>
                    </div>
                    <span className="text-xs text-muted-foreground">
                      {op.itemsDone}/{op.itemsTotal} items
                      {op.itemsFailed > 0 && <span className="text-red-600 ml-1">({op.itemsFailed} failed)</span>}
                    </span>
                  </div>
                  <div className="w-full h-1.5 bg-muted rounded-full overflow-hidden">
                    <div
                      className="h-full bg-indigo-500 rounded-full transition-all"
                      style={{ width: op.itemsTotal > 0 ? `${(op.itemsDone / op.itemsTotal) * 100}%` : '0%' }}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Recent Node Changes */}
      <div className="rounded-lg border bg-card p-4 space-y-2">
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
          Recent Node State Changes <span className="font-normal text-muted-foreground">(last 15 min)</span>
        </h2>
        {recentNodeChanges.length === 0 ? (
          <p className="text-sm text-muted-foreground">No node state changes in the last 15 minutes</p>
        ) : (
          <div className="flex gap-3 overflow-x-auto pb-1">
            {recentNodeChanges.map((change, i) => (
              <div key={i} className="flex-shrink-0 rounded border bg-muted/40 px-3 py-2 text-xs space-y-1 min-w-[140px]">
                <p className="font-semibold truncate">{change.nodeId}</p>
                <p className="text-muted-foreground">
                  {change.fromState ? `${change.fromState} → ` : ''}{change.toState}
                </p>
                <p className="text-muted-foreground">{change.trigger}</p>
                <p className="text-muted-foreground">
                  {formatDistanceToNow(new Date(change.occurredAt), { addSuffix: true })}
                </p>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
