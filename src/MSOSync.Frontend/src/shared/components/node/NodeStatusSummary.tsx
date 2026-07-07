import type { ConnectivityStatusName, NodeLifecycleState } from '../../types/lifecycle';
import { LifecycleBadge } from './LifecycleBadge';
import { ConnectivityBadge } from './ConnectivityBadge';
import { MaintenanceBadge } from './MaintenanceBadge';

export interface NodeStatusSummaryProps {
  lifecycle: NodeLifecycleState;
  connectivity: ConnectivityStatusName;
  connectivityReason?: string | null;
  maintenance?: boolean;
  maintenanceReason?: string | null;
}

/** The single composite renderer for node state — used everywhere (spec §11.1). */
export function NodeStatusSummary(p: NodeStatusSummaryProps) {
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      <LifecycleBadge state={p.lifecycle} />
      <ConnectivityBadge status={p.connectivity} reason={p.connectivityReason} />
      <MaintenanceBadge active={p.maintenance ?? false} reason={p.maintenanceReason} />
    </span>
  );
}
