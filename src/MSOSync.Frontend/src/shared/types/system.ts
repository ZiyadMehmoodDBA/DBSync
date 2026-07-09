export type HealthLevel = 'Healthy' | 'Degraded' | 'Critical' | 'Unknown';
export type WarningSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

export interface OverviewHealthWidget {
  clusterHealth: HealthLevel;
  workerHealth: HealthLevel;
  nodeHealth: HealthLevel;
}

export interface OverviewOperationsWidget {
  running: number;
  succeededToday: number;
  failedToday: number;
  queued: number;
}

export interface OverviewNodesWidget {
  total: number;
  active: number;
  offline: number;
  maintenance: number;
  degraded: number;
  pendingRegistrations: number;
}

export interface OverviewConfigurationWidget {
  driftedCount: number;
  updateAvailableCount: number;
  failedCount: number;
}

export interface OverviewWarningDto {
  type: string;
  severity: WarningSeverity;
  title: string;
  description: string;
  targetRoute: string;
  correlationId: string | null;
}

export interface OverviewEventDto {
  eventId: string;
  occurredAt: string;
  category: string;
  summary: string;
  nodeId: string | null;
  correlationId: string | null;
  deepLink: string | null;
}

export interface OverviewSystemWidget {
  version: string;
  databaseMigration: string;
  environment: string;
  uptime: string;
  signalRStatus: string;
  lastRefreshedAt: string;
}

export interface OverviewDto {
  health: OverviewHealthWidget;
  operations: OverviewOperationsWidget;
  nodes: OverviewNodesWidget;
  configuration: OverviewConfigurationWidget;
  warnings: OverviewWarningDto[];
  recentActivity: OverviewEventDto[];
  system: OverviewSystemWidget;
  lastRefreshedAt: string;
}

export interface SystemInfoDto {
  version: string;
  buildDate: string | null;
  gitCommit: string | null;
  dotNetRuntime: string;
  operatingSystem: string;
  databaseMigration: string;
  edition: string;
  environment: string;
  serverTime: string;
  processUptime: string;
}

export type WorkerStateType =
  | 'Running'
  | 'Idle'
  | 'Warning'
  | 'Failed'
  | 'Delayed'
  | 'Disabled';

export interface WorkerTickDto {
  tickId: number;
  startedAt: string;
  completedAt: string | null;
  durationMs: number | null;
  trigger: string;
  isSuccess: boolean;
  errorMessage: string | null;
}

export interface WorkerStatusDto {
  workerId: string;
  workerName: string;
  workerState: WorkerStateType;
  lastRunAt: string | null;
  nextExpectedAt: string | null;
  avgDurationMs: number | null;
  executionCount: number;
  failureCount: number;
  lastFailureAt: string | null;
  successRatePct: number | null;
  maxDurationMs: number | null;
  recentTicks: WorkerTickDto[];
}

export interface HealthContributionDto {
  contributor: string;
  level: HealthLevel;
  detail: string | null;
  latencyMs: number | null;
}
