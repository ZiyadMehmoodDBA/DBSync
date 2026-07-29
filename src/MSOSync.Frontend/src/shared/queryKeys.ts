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
  eventsInfinite: (filter: Omit<EventFilter, 'page'>) =>
    ['events', 'infinite', filter] as const,
  event: (id: number) => ['event', id] as const,

  incomingBatches: (filter: IncomingBatchFilter) => ['incoming-batches', filter] as const,
  incomingBatchesInfinite: (filter: Omit<IncomingBatchFilter, 'page'>) =>
    ['incoming-batches', 'infinite', filter] as const,
  outgoingBatchesBase: () => ['outgoing-batches'] as const,
  outgoingBatches: (filter: OutgoingBatchFilter) => ['outgoing-batches', filter] as const,
  outgoingBatchesInfinite: (filter: Omit<OutgoingBatchFilter, 'page'>) =>
    ['outgoing-batches', 'infinite', filter] as const,
  batchErrors: (filter: BatchErrorFilter) => ['batch-errors', filter] as const,

  nodes: (pageNumber = 1, pageSize = 50) => ['nodes', pageNumber, pageSize] as const,
  node: (id: string) => ['node', id] as const,

  topologySummary: () => ['topology-summary'] as const,
  topologyGroups: () => ['topology-groups'] as const,
  topologyGroupNodes: (groupId: string) => ['topology-group-nodes', groupId] as const,
  topologyGraph: () => ['topology-graph'] as const,

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
  auditLogInfinite: (filter: Omit<AuditFilter, 'page'>) =>
    ['audit', 'infinite', filter] as const,
  auditSummary: (from: string, to: string) => ['audit-summary', from, to] as const,
  locks: () => ['locks'] as const,

  userPreferences: () => ['user-preferences'] as const,

  permissions:      () => ['permissions'] as const,
  permissionCatalog: () => ['permission-catalog'] as const,
  roles:             () => ['roles'] as const,
  role:              (name: string) => ['roles', name] as const,

  exportJobs: () => ['export-jobs'] as const,

  nodeState:            (id: string) => ['node-state', id] as const,
  nodeTransitions:      (id: string) => ['node-transitions', id] as const,
  nodeLifecycleHistory: (id: string, filter?: unknown) =>
    filter ? ['node-lifecycle-history', id, filter] as const : ['node-lifecycle-history', id] as const,

  configurationTemplates: (filter?: string) =>
    filter ? ['configuration-templates', filter] as const : ['configuration-templates'] as const,
  configurationTemplate: (id: string) => ['configuration-template', id] as const,
  configurationTemplateVersions: (id: string) => ['configuration-template-versions', id] as const,
  configurationTemplateVersion: (id: string, v: number) =>
    ['configuration-template-version', id, v] as const,

  nodeConfiguration: (nodeId: string) => ['node-configuration', nodeId] as const,
  nodeConfigurationHistory: (nodeId: string) => ['node-configuration-history', nodeId] as const,
  nodeScope: (nodeId: string) => ['node-scope', nodeId] as const,

  driftSummary: (filter?: Record<string, unknown>) =>
    filter ? ['drift-summary', filter] as const : ['drift-summary'] as const,
  driftNodes: (filter?: Record<string, unknown>) =>
    filter ? ['drift-nodes', filter] as const : ['drift-nodes'] as const,
  configurationSummary: () => ['configuration-summary'] as const,
  rolloutStatus: (rolloutId: string) => ['rollout-status', rolloutId] as const,

  notifications:       (filter?: string) => ['notifications', filter ?? 'all'] as const,
  notificationsUnread: () => ['notifications', 'unread-count'] as const,

  plugins: {
    all:     () => ['plugins'] as const,
    detail:  (id: string) => ['plugins', id] as const,
    summary: () => ['plugins', 'summary'] as const,
  },

  marketplace: {
    search: (params: { query?: string; category?: string; page: number; pageSize: number }) =>
      ['marketplace', 'search', params] as const,
    detail: (id: string) =>
      ['marketplace', 'plugin', id] as const,
    versions: (id: string) =>
      ['marketplace', 'versions', id] as const,
    updates: () =>
      ['marketplace', 'updates'] as const,
    updateCount: () =>
      ['marketplace', 'update-count'] as const,
  },

  healthScores: () => ['health-scores'] as const,
  sloStatus:    () => ['slo-status'] as const,
};
