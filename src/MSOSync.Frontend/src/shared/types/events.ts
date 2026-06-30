export interface EventSummaryDto {
  eventId: number;
  triggerId: string;
  sourceNodeId: string;
  channelId: string;
  eventType: string;
  tableName: string;
  batchId?: number;
  createTime: string;
  isProcessed: boolean;
}

export interface EventDetailDto extends EventSummaryDto {
  pkData: string;
  rowData: string;
  transactionId?: string;
}

export interface EventFilter {
  sourceNodeId?: string;
  triggerId?: string;
  channelId?: string;
  eventType?: string;
  isProcessed?: boolean;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}
