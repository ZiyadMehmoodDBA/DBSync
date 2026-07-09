export type OperationStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type OperationResult = 'Success' | 'PartialSuccess' | 'Failure' | 'Cancelled';
export type OperationType = 'Export' | 'Rollout' | 'Decommission' | 'Recovery';

export interface OperationDto {
  operationId: string;
  operationType: OperationType;
  status: OperationStatus;
  result: OperationResult | null;
  progressPercent: number | null;
  progressMessage: string | null;
  queuePosition: number | null;
  correlationId: string | null;
  initiatedBy: string | null;
  startedAt: string;
  completedAt: string | null;
  canCancel: boolean;
  canRetry: boolean;
  summary: string | null;
}

export interface OperationPageDto {
  items: OperationDto[];
  totalCount: number | null;
  nextCursor: string | null;
}

export interface OperationFilter {
  types?: OperationType[];
  statuses?: OperationStatus[];
  from?: string;
  to?: string;
  pageSize?: number;
  cursor?: string;
}
