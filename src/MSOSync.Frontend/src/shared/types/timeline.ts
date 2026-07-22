export type OperationType =
  | 'Export' | 'Rollout' | 'Decommission' | 'Recovery'
  | 'RollingMaintenance' | 'RollingUpgrade' | 'BatchReplay';

export interface OperationTimelineItemDto {
  operationId:     string;
  type:            string;
  status:          string;
  label:           string | null;
  startedAt:       string;   // ISO UTC
  completedAt:     string | null; // ISO UTC, null = still running
  progressPercent: number | null;
}

export interface OperationTimelineDto {
  items:          OperationTimelineItemDto[];
  from:           string;
  to:             string;
  hasMore:        boolean;
  returnedCount:  number;
}
