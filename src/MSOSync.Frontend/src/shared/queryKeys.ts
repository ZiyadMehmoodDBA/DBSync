import type {
  EventFilter,
  IncomingBatchFilter,
  OutgoingBatchFilter,
  BatchErrorFilter,
  AuditFilter,
  UserFilter,
} from './types';

export const queryKeys = {
  dashboardSummary: () => ['dashboard-summary'] as const,
  dashboardActivity: (page: number) => ['dashboard-activity', page] as const,

  events: (filter: EventFilter) => ['events', filter] as const,
  event: (id: number) => ['event', id] as const,

  incomingBatches: (filter: IncomingBatchFilter) => ['incoming-batches', filter] as const,
  outgoingBatches: (filter: OutgoingBatchFilter) => ['outgoing-batches', filter] as const,
  batchErrors: (filter: BatchErrorFilter) => ['batch-errors', filter] as const,

  nodes: () => ['nodes'] as const,
  node: (id: string) => ['node', id] as const,

  topologySummary: () => ['topology-summary'] as const,
  topologyGroups: () => ['topology-groups'] as const,
  topologyGroupNodes: (groupId: string) => ['topology-group-nodes', groupId] as const,

  metricsSummary: () => ['metrics-summary'] as const,
  nodeMetrics: () => ['node-metrics'] as const,
  channelMetrics: () => ['channel-metrics'] as const,
  runtimeMetrics: () => ['runtime-metrics'] as const,

  channels: () => ['channels'] as const,
  triggers: () => ['triggers'] as const,
  routers: () => ['routers'] as const,

  users: (filter: UserFilter) => ['users', filter] as const,
  parameters: () => ['parameters'] as const,
  parameterDescriptors: () => ['parameter-descriptors'] as const,

  auditLog: (filter: AuditFilter) => ['audit', filter] as const,
  locks: () => ['locks'] as const,
};
