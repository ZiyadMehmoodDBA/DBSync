import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}));

import { toast } from 'sonner';
import { routeToToast } from './notifications';
import { OperationsEventType, type OperationsEvent } from './types';

function makeEvent(
  type: OperationsEventType,
  overrides: Partial<OperationsEvent> = {},
): OperationsEvent {
  return {
    type,
    nodeId: 'node-1',
    nodeLabel: 'NodeAlpha',
    previousStatus: null,
    currentStatus: null,
    occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 0)).toISOString(),
    groupId: null,
    ...overrides,
  };
}

describe('routeToToast', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Reset the dedup map between tests by varying timestamps
  });

  it('NodeHealthChanged Reachable → shows success toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Degraded',
      currentStatus: 'Reachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 1)).toISOString(),
    }));
    expect(toast.success).toHaveBeenCalledWith('Node NodeAlpha is reachable again.');
  });

  it('NodeHealthChanged * → Degraded shows warning toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Reachable',
      currentStatus: 'Degraded',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 2)).toISOString(),
    }));
    expect(toast.warning).toHaveBeenCalledWith('Node NodeAlpha is degraded.');
  });

  it('NodeHealthChanged * → Unreachable shows error toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Reachable',
      currentStatus: 'Unreachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 3)).toISOString(),
    }));
    expect(toast.error).toHaveBeenCalledWith('Node NodeAlpha is unreachable.');
  });

  it('NodeHealthChanged Unreachable → Unreachable shows no toast (already unreachable)', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      previousStatus: 'Unreachable',
      currentStatus: 'Unreachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 4)).toISOString(),
    }));
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.warning).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('NodeApproved → shows success toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 5)).toISOString(),
    }));
    expect(toast.success).toHaveBeenCalledWith('Node NodeAlpha approved.');
  });

  it('NodeRejected → shows warning toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeRejected, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 6)).toISOString(),
    }));
    expect(toast.warning).toHaveBeenCalledWith('Node NodeAlpha registration rejected.');
  });

  it('NodeDisabled → shows warning toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeDisabled, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 7)).toISOString(),
    }));
    expect(toast.warning).toHaveBeenCalledWith('Node NodeAlpha disabled.');
  });

  it('NodeEnabled → shows info toast', () => {
    routeToToast(makeEvent(OperationsEventType.NodeEnabled, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 8)).toISOString(),
    }));
    expect(toast.info).toHaveBeenCalledWith('Node NodeAlpha re-enabled.');
  });

  it('SyncCycleCompleted → no toast (silent)', () => {
    routeToToast(makeEvent(OperationsEventType.SyncCycleCompleted, {
      occurredAt: new Date(Date.UTC(2026, 0, 1, 12, 0, 9)).toISOString(),
    }));
    expect(toast.success).not.toHaveBeenCalled();
    expect(toast.warning).not.toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.info).not.toHaveBeenCalled();
  });

  it('dedup: same key in same bucket → second call suppressed', () => {
    const ts = new Date(Date.UTC(2026, 0, 1, 13, 0, 10)).toISOString();
    const evt = makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-dedup',
      nodeLabel: 'DedupNode',
      currentStatus: null,
      occurredAt: ts,
    });

    routeToToast(evt);
    routeToToast(evt);

    expect(toast.success).toHaveBeenCalledTimes(1);
  });

  it('dedup: same key in next bucket → shown again', () => {
    const bucket1 = new Date(Date.UTC(2026, 0, 1, 14, 0, 0)).toISOString();
    const bucket2 = new Date(Date.UTC(2026, 0, 1, 14, 0, 31)).toISOString(); // +31s = next bucket

    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-bucket',
      nodeLabel: 'BucketNode',
      currentStatus: null,
      occurredAt: bucket1,
    }));
    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-bucket',
      nodeLabel: 'BucketNode',
      currentStatus: null,
      occurredAt: bucket2,
    }));

    expect(toast.success).toHaveBeenCalledTimes(2);
  });

  it('dedup: same node different currentStatus → different key → both shown', () => {
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      nodeId: 'node-status',
      previousStatus: 'Reachable',
      currentStatus: 'Degraded',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 15, 0, 0)).toISOString(),
    }));
    routeToToast(makeEvent(OperationsEventType.NodeHealthChanged, {
      nodeId: 'node-status',
      previousStatus: 'Degraded',
      currentStatus: 'Unreachable',
      occurredAt: new Date(Date.UTC(2026, 0, 1, 15, 0, 1)).toISOString(),
    }));

    expect(toast.warning).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledTimes(1);
  });

  it('nodeLabel falls back to nodeId when null', () => {
    routeToToast(makeEvent(OperationsEventType.NodeApproved, {
      nodeId: 'node-fallback',
      nodeLabel: null,
      occurredAt: new Date(Date.UTC(2026, 0, 1, 16, 0, 0)).toISOString(),
    }));
    expect(toast.success).toHaveBeenCalledWith('Node node-fallback approved.');
  });
});
