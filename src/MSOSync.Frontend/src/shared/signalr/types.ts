export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected';

export interface SignalRContextValue {
  connectionState: ConnectionState;
  lastConnectedAt?: Date;
  lastDisconnectedAt?: Date;
}

export const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000, 30_000] as const;

export interface OperationsEvent {
  type: OperationsEventType;
  nodeId: string;
  nodeLabel: string | null;
  previousStatus: string | null;
  currentStatus: string | null;
  occurredAt: string; // ISO 8601
  groupId: string | null;
}

export const OperationsEventType = {
  NodeHealthChanged:  'NodeHealthChanged',
  NodeApproved:       'NodeApproved',
  NodeRejected:       'NodeRejected',
  NodeDisabled:       'NodeDisabled',
  NodeEnabled:        'NodeEnabled',
  SyncCycleCompleted: 'SyncCycleCompleted',
} as const;

export type OperationsEventType = (typeof OperationsEventType)[keyof typeof OperationsEventType];
