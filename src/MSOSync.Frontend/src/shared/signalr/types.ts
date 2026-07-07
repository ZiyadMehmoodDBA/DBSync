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
  correlationId?: string | null;
  trigger?: string | null;
}

export const OperationsEventType = {
  NodeHealthChanged:   'NodeHealthChanged',
  NodeApproved:        'NodeApproved',
  NodeRejected:        'NodeRejected',
  NodeDisabled:        'NodeDisabled',
  NodeEnabled:         'NodeEnabled',
  SyncCycleCompleted:  'SyncCycleCompleted',
  NodeLifecycleChanged:   'NodeLifecycleChanged',
  NodeMaintenanceChanged: 'NodeMaintenanceChanged',
} as const;

export type OperationsEventType = (typeof OperationsEventType)[keyof typeof OperationsEventType];

export interface PermissionEvent {
  roleName: string;
  action: string;
  occurredAt: string;
}

export interface ExportJobEvent {
  jobId:           string;
  status:          string;
  progressPercent: number;
  rowCount:        number | null;
}
