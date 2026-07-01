import { toast } from 'sonner';
import { OperationsEventType, type OperationsEvent } from './types';

const seen = new Map<string, true>();

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
