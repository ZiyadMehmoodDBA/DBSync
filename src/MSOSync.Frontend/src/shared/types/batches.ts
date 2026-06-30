export interface IncomingBatchSummaryDto {
  batchId: number;
  sourceNodeId: string;
  channelId: string;
  status: string;
  rowCount: number;
  receivedTime: string;
  appliedTime?: string;
  applyTimeMs?: number;
}

export interface OutgoingBatchDto {
  batchId: number;
  status: string;
  nodeId: string;
  channelId: string;
  createTime: string;
  sentTime?: string;
  ackTime?: string;
  retryCount: number;
  rowCount: number;
  error?: string;
}

export interface BatchErrorDetailDto {
  errorId: number;
  batchId: number;
  conflictType: string;
  severity: string;
  createTime: string;
  detail?: string;
}

export interface IncomingBatchFilter {
  sourceNodeId?: string;
  channelId?: string;
  status?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}

export interface OutgoingBatchFilter {
  status?: string;
  nodeId?: string;
  channelId?: string;
  sortBy?: 'createTime' | 'batchId' | 'status';
  sortDirection?: 'asc' | 'desc';
  page: number;
  pageSize: number;
}

export interface BatchErrorFilter {
  batchId?: number;
  conflictType?: string;
  severity?: string;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}
