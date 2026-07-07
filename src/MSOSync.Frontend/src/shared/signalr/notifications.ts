import { toast } from 'sonner';
import { OperationsEventType, type OperationsEvent } from './types';

const seen = new Map<string, true>();

// Toast dedupe by CorrelationId (spec §8: events idempotent — duplicate delivery, no duplicate toasts)
const seenCorrelationIds = new Set<string>();

export function dedupe(correlationId: string | null | undefined): boolean {
  if (!correlationId) return false;
  if (seenCorrelationIds.has(correlationId)) return true;
  seenCorrelationIds.add(correlationId);
  if (seenCorrelationIds.size > 500) {
    const first = seenCorrelationIds.values().next().value;
    if (first) seenCorrelationIds.delete(first);
  }
  return false;
}

/** Exposed for test teardown only — clears the correlationId dedupe set. */
export function _resetDedupeForTests(): void {
  seenCorrelationIds.clear();
}

// Toast catalogue (spec §8): Activated, Enabled, Disabled, Maintenance Started/Ended,
// Decommission Started/Completed. Connectivity = silent badge updates, never a toast.
// (Recovery Approved toast comes from the approve mutation hook, not SignalR — no state change event.)
export function lifecycleToastMessage(event: OperationsEvent): string | null {
  if (event.type === OperationsEventType.NodeMaintenanceChanged) {
    return event.currentStatus === 'MaintenanceOn'
      ? `Node ${event.nodeId}: maintenance started`
      : `Node ${event.nodeId}: maintenance ended`;
  }
  if (event.type !== OperationsEventType.NodeLifecycleChanged) return null;
  switch (event.currentStatus) {
    case 'Active':
      return event.trigger === 'Activation'
        ? `Node ${event.nodeId} activated`
        : `Node ${event.nodeId} enabled`;
    case 'Disabled':         return `Node ${event.nodeId} disabled`;
    case 'Decommissioning':  return `Node ${event.nodeId}: decommission started`;
    case 'Decommissioned':   return `Node ${event.nodeId}: decommission completed`;
    default:                 return null;   // Recovery entry etc. — queue badge, not a toast
  }
}

export function routeToToast(event: OperationsEvent): void {
  const label = event.nodeLabel ?? event.nodeId;

  switch (event.type) {
    case OperationsEventType.NodeHealthChanged: {
      const message = resolveHealthMessage(label, event.previousStatus, event.currentStatus);
      if (message) showDeduped(event, message.text, message.severity);
      break;
    }
    case OperationsEventType.NodeApproved:
      showDeduped(event, `Node ${label} approved.`, 'success');
      break;
    case OperationsEventType.NodeRejected:
      showDeduped(event, `Node ${label} registration rejected.`, 'warning');
      break;
    case OperationsEventType.NodeDisabled:
      showDeduped(event, `Node ${label} disabled.`, 'warning');
      break;
    case OperationsEventType.NodeEnabled:
      showDeduped(event, `Node ${label} re-enabled.`, 'info');
      break;
    case OperationsEventType.SyncCycleCompleted:
      // Silent cache invalidation — no toast
      break;
    case OperationsEventType.NodeLifecycleChanged:
    case OperationsEventType.NodeMaintenanceChanged: {
      if (dedupe(event.correlationId)) return;
      const msg = lifecycleToastMessage(event);
      if (msg) toast.info(msg);
      break;
    }
  }
}

function resolveHealthMessage(
  label: string,
  previousStatus: string | null,
  currentStatus: string | null,
): { text: string; severity: 'success' | 'warning' | 'error' } | null {
  if (currentStatus === 'Reachable') {
    return { text: `Node ${label} is reachable again.`, severity: 'success' };
  }
  if (currentStatus === 'Degraded') {
    return { text: `Node ${label} is degraded.`, severity: 'warning' };
  }
  if (currentStatus === 'Unreachable' && previousStatus !== 'Unreachable') {
    return { text: `Node ${label} is unreachable.`, severity: 'error' };
  }
  return null;
}

function showDeduped(
  event: OperationsEvent,
  message: string,
  severity: 'success' | 'warning' | 'error' | 'info',
): void {
  const bucket = Math.floor(new Date(event.occurredAt).getTime() / 30_000);
  const key    = `${event.type}:${event.nodeId}:${event.currentStatus}:${bucket}`;

  if (seen.has(key)) return;

  if (seen.size >= 1000) seen.clear();
  seen.set(key, true);

  toast[severity](message);
}
