export interface NodeStateCountsDto {
  total: number;
  active: number;
  maintenance: number;
  draining: number;
  offline: number;
}

export interface OperationCountsDto {
  running: number;
  pending: number;
  succeededToday: number;
  failedToday: number;
}

export interface ActiveOperationSummaryDto {
  operationId: string;
  type: string;
  status: string;
  nodeId: string | null;
  progressPercent: number | null;
  progressMessage: string | null;
  startedAt: string;
}

export interface RollingWaveSummaryDto {
  operationId: string;
  mode: string;
  status: string;
  currentWave: number;
  totalWaves: number;
  nodesDone: number;
  nodesTotal: number;
  nodesFailed: number;
}

export interface ReplayOperationSummaryDto {
  operationId: string;
  replayMode: string;
  status: string;
  itemsDone: number;
  itemsTotal: number;
  itemsFailed: number;
}

export interface NodeStateChangeDto {
  nodeId: string;
  fromState: string | null;
  toState: string;
  trigger: string;
  occurredAt: string;
}

export interface ClusterSummaryDto {
  nodeStates: NodeStateCountsDto;
  operationCounts: OperationCountsDto;
  activeOperations: ActiveOperationSummaryDto[];
  activeRollingOps: RollingWaveSummaryDto[];
  activeReplays: ReplayOperationSummaryDto[];
  recentNodeChanges: NodeStateChangeDto[];
}
