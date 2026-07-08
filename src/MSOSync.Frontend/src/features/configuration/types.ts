export type ConfigurationState =
  | 'None'
  | 'Current'
  | 'UpdateAvailable'
  | 'Applying'
  | 'Drifted'
  | 'Failed'
  | 'Unknown';

export type TemplateStatus = 'Draft' | 'Published' | 'Archived';

export type OverrideSource = 'Manual' | 'Imported' | 'API';

export interface ConfigurationSettings {
  heartbeatIntervalSeconds: number;
  transportMode: string;
  maxRetryAttempts: number;
  retryBackoffSeconds: number;
  batchSizeLimit: number;
  minimumAgentVersion: string | null;
  featureFlags: Record<string, boolean>;
  channelIds: string[];
  routerIds: string[];
  triggerIds: string[];
}

export interface TemplateSummaryDto {
  id: string;
  name: string;
  description: string | null;
  status: TemplateStatus;
  currentPublishedVersion: number | null;
  latestDraftVersion: number | null;
  updatedAt: string;
}

export interface TemplateVersionSummaryDto {
  id: string;
  versionNumber: number;
  isDraft: boolean;
  templateContentHash: string | null;
  schemaVersion: number;
  publishedAt: string | null;
}

export interface TemplateVersionDto extends TemplateVersionSummaryDto {
  templateId: string;
  settings: ConfigurationSettings;
}

export interface TemplateDto extends TemplateSummaryDto {
  createdAt: string;
  versions: TemplateVersionSummaryDto[];
}

export interface NodeOverrideDto {
  id: string;
  settingKey: string;
  settingValue: string;
  overrideSource: OverrideSource;
  updatedAt: string;
}

export interface NodeConfigurationDto {
  nodeId: string;
  assignedTemplateId: string | null;
  assignedTemplateVersion: number | null;
  appliedTemplateVersion: number | null;
  expectedEffectiveHash: string | null;
  appliedEffectiveHash: string | null;
  configurationState: ConfigurationState | null;
  lastAppliedAt: string | null;
  effectiveSettings: ConfigurationSettings | null;
  overrides: NodeOverrideDto[];
}

export interface ConfigurationHistoryEventDto {
  id: string;
  nodeId: string;
  eventType: string;
  templateId: string | null;
  templateVersion: number | null;
  configurationHash: string | null;
  correlationId: string | null;
  actorId: string | null;
  occurredAt: string;
  notes: string | null;
}

export interface DriftSummaryDto {
  noneCount: number;
  currentCount: number;
  updateAvailableCount: number;
  applyingCount: number;
  driftedCount: number;
  failedCount: number;
  unknownCount: number;
}

export interface DriftNodeDto {
  nodeId: string;
  nodeName: string;
  groupId: string | null;
  assignedTemplateId: string | null;
  assignedTemplateName: string | null;
  assignedTemplateVersion: number | null;
  appliedTemplateVersion: number | null;
  expectedEffectiveHash: string | null;
  appliedEffectiveHash: string | null;
  configurationState: ConfigurationState | null;
  configurationStatusReportedAt: string | null;
}

export interface RolloutDto {
  id: string;
  status: 'Queued' | 'InProgress' | 'Completed' | 'Failed' | 'Cancelled';
  templateId: string;
  templateVersion: number;
  targetNodeCount: number;
  appliedCount: number;
  failedCount: number;
  pendingCount: number;
  progressPercent: number;
  initiatedBy: string;
  startedAt: string;
  completedAt: string | null;
}

// Request types
export interface CreateTemplateRequest {
  name: string;
  description?: string;
  initialSettings: ConfigurationSettings;
}

export interface UpdateDraftRequest {
  settings: ConfigurationSettings;
}

export interface AssignRequest {
  templateId: string;
  version: number;
}

export interface SetOverrideRequest {
  key: string;
  value: string;
  source: OverrideSource;
}

export interface StartRolloutRequest {
  templateId: string;
  templateVersion: number;
  nodeIds: string[];
}
