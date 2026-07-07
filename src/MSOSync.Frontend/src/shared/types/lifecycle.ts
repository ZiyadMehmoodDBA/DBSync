export type NodeLifecycleState =
  | 'PendingApproval'
  | 'PendingRegistration'
  | 'Active'
  | 'Recovery'
  | 'Disabled'
  | 'Decommissioning'
  | 'Decommissioned'
  | 'Rejected';

export type ConnectivityStatusName = 'Unknown' | 'Reachable' | 'Degraded' | 'Unreachable';

export type ConnectivityReason =
  | 'NotEvaluated'
  | 'NoHeartbeat'
  | 'Healthy'
  | 'HeartbeatStale'
  | 'HeartbeatExpired'
  | 'ProbeFailed'
  | 'ProbeFailures'
  | 'PendingActivation';

export type LifecycleTrigger =
  | 'Manual'
  | 'Registration'
  | 'Activation'
  | 'Recovery'
  | 'System'
  | 'Timeout'
  | 'Migration';

export interface NodeStateDto {
  nodeId: string;
  lifecycleState: NodeLifecycleState;
  connectivityStatus: ConnectivityStatusName;
  connectivityReason: string | null;
  lastHeartbeatUtc: string | null;
  lastProbeUtc: string | null;
  maintenanceMode: boolean;
  maintenanceReason: string | null;
  maintenanceUntil: string | null;
  decommissionInProgress: boolean;
  drainProgressPercent: number | null;
  decommissionGraceUntil: string | null;
}

export type LifecycleDangerLevel = 'Normal' | 'Critical';

export type LifecycleAction =
  | 'Enable'
  | 'Disable'
  | 'StartMaintenance'
  | 'EndMaintenance'
  | 'Decommission'
  | 'ForceCompleteDecommission';

export interface TransitionActionDto {
  action: LifecycleAction;
  requiresReason: boolean;
  requiresConfirmation: boolean;
  dangerLevel: LifecycleDangerLevel;
}

export interface TransitionsDto {
  currentState: NodeLifecycleState;
  allowedTransitions: TransitionActionDto[];
}

export interface LifecycleHistoryDto {
  historyId: number;
  nodeId: string;
  fromState: NodeLifecycleState | null;
  toState: NodeLifecycleState;
  trigger: LifecycleTrigger;
  reason: string | null;
  actor: string;
  correlationId: string | null;
  metadataJson: string | null;
  occurredAt: string;
}

export interface LifecycleHistoryPage {
  items: LifecycleHistoryDto[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface LifecycleHistoryFilter {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  trigger?: LifecycleTrigger;
}
