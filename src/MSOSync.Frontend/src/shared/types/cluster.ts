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

// Phase 2B.4 — Cluster Health Trends
export interface HealthBucketDto {
  bucketStart: string;
  reachableCount: number;
  degradedCount: number;
  unreachableCount: number;
  totalNodes: number;
  transitionCount: number;
}

export interface NodeProbeStatsDto {
  nodeId: string;
  connectivityStatus: string;
  lastProbeLatencyMs: number | null;
  consecutiveProbeFailures: number;
  uptimePct: number;
}

export interface ClusterHealthTrendDto {
  window: string;
  bucketCount: number;
  buckets: HealthBucketDto[];
  nodeProbeStats: NodeProbeStatsDto[];
}

// Phase 2B.4 — Recovery Dashboard
export interface ReplayOpRefDto {
  operationId: string;
  status: string;
  itemsDone: number;
  itemsTotal: number;
}

export interface ActiveRecoveryDto {
  nodeId: string;
  failureDetectedAt: string | null;
  recoveryStartedAt: string;
  elapsedMinutes: number;
  associatedReplayOps: ReplayOpRefDto[];
}

export interface CompletedRecoveryDto {
  nodeId: string;
  failureDetectedAt: string | null;
  recoveryStartedAt: string;
  restoredAt: string;
  rtoMinutes: number;
}

export interface RecoverySummaryDto {
  activeCount: number;
  avgRtoMinutes: number | null;
  maxRtoMinutes: number | null;
  completedLast30Days: number;
}

export interface RecoveryDashboardDto {
  summary: RecoverySummaryDto;
  activeRecoveries: ActiveRecoveryDto[];
  recentCompletedRecoveries: CompletedRecoveryDto[];
}
