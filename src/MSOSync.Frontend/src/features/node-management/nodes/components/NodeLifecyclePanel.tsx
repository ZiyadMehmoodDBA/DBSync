import { useNodeState } from '../../../../shared/hooks/useNodeLifecycle';
import { NodeStatusSummary } from '../../../../shared/components/node';
import { LifecycleHistoryTimeline } from './LifecycleHistoryTimeline';
import { formatRelativeTime } from '../../../../shared/utils/date';

export function NodeLifecyclePanel({ nodeId }: { nodeId: string }) {
  const { data: state, isLoading } = useNodeState(nodeId);
  if (isLoading || !state) return <p className="p-4 text-sm text-neutral-500">Loading node state…</p>;

  return (
    <div className="space-y-4 p-4">
      <div className="space-y-2">
        <NodeStatusSummary
          lifecycle={state.lifecycleState}
          connectivity={state.connectivityStatus}
          connectivityReason={state.connectivityReason}
          maintenance={state.maintenanceMode}
          maintenanceReason={state.maintenanceReason}
        />
        <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
          <dt className="text-neutral-500">Last heartbeat</dt>
          <dd>{state.lastHeartbeatUtc ? formatRelativeTime(state.lastHeartbeatUtc) : '—'}</dd>
          <dt className="text-neutral-500">Last probe</dt>
          <dd>{state.lastProbeUtc ? formatRelativeTime(state.lastProbeUtc) : '—'}</dd>
          {state.maintenanceMode && (
            <>
              <dt className="text-neutral-500">Maintenance until</dt>
              <dd>{state.maintenanceUntil ? new Date(state.maintenanceUntil).toLocaleString() : 'open-ended'}</dd>
            </>
          )}
        </dl>
        {state.decommissionInProgress && (
          <div className="space-y-1">
            <div className="flex justify-between text-xs text-neutral-500">
              <span>Draining…</span>
              <span>
                {state.drainProgressPercent !== null ? `${state.drainProgressPercent}%` : 'in progress'}
                {state.decommissionGraceUntil &&
                  ` · grace until ${new Date(state.decommissionGraceUntil).toLocaleString()}`}
              </span>
            </div>
            <div className="h-2 w-full rounded bg-neutral-200 dark:bg-neutral-800">
              <div
                className="h-2 rounded bg-purple-500 transition-all"
                style={{ width: `${state.drainProgressPercent ?? 5}%` }}
              />
            </div>
          </div>
        )}
      </div>
      <div>
        <h3 className="mb-2 text-sm font-semibold">Lifecycle timeline</h3>
        <LifecycleHistoryTimeline nodeId={nodeId} />
      </div>
    </div>
  );
}
