export type ReplayMode = 'FailedDelivery' | 'MissedData' | 'Both';

export type ReplayItemStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed' | 'Skipped';

export interface CreateReplayOperationRequest {
  nodeId: string;
  replayMode: ReplayMode;
  fromTime: string;    // ISO datetime
  toTime: string;      // ISO datetime
  channelIds?: string[] | null;
  batchIds?: number[] | null;
}

export interface ReplayOperationCreatedDto {
  operationId: string;
  itemCount: number;
}

export interface ReplayOperationDetailDto {
  operationId: string;
  status: string;
  result?: string | null;
  nodeId: string;
  replayMode: ReplayMode;
  fromTime: string;
  toTime: string;
  channelIds?: string[] | null;
  batchIds?: number[] | null;
  totalItems: number;
  completedItems: number;
  failedItems: number;
  skippedItems: number;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface ReplayItemDto {
  itemId: string;
  nodeId: string;
  channelId: string;
  eventCount: number;
  status: ReplayItemStatus;
  errorMessage?: string | null;
  sourceBatchId?: number | null;
  replayBatchId?: number | null;
}

export interface ReplayItemPage {
  items: ReplayItemDto[];
  nextCursor?: string | null;
  hasMore: boolean;
  totalCount?: number | null;
}
