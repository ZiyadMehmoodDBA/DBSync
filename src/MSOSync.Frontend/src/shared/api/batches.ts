import client from './client';
import type {
  IncomingBatchSummaryDto,
  OutgoingBatchDto,
  BatchErrorDetailDto,
  IncomingBatchFilter,
  OutgoingBatchFilter,
  BatchErrorFilter,
} from '../types';
import type { PagedResult, CursorPageResult } from '../types/common';

export type CursorIncomingBatchFilter = Omit<IncomingBatchFilter, 'page'> & {
  cursor?: string;
  includeTotalCount?: boolean;
};

export async function getIncomingBatches(
  filter: CursorIncomingBatchFilter,
  options?: { signal?: AbortSignal },
): Promise<CursorPageResult<IncomingBatchSummaryDto>> {
  const { data } = await client.get<CursorPageResult<IncomingBatchSummaryDto>>('/incoming-batches', {
    params: filter,
    signal: options?.signal,
  });
  return data;
}

export async function getOutgoingBatches(
  filter: OutgoingBatchFilter,
  options?: { signal?: AbortSignal },
): Promise<PagedResult<OutgoingBatchDto>> {
  const { data } = await client.get<PagedResult<OutgoingBatchDto>>('/batches', {
    params: filter,
    signal: options?.signal,
  });
  return data;
}

export async function getBatchErrors(
  filter: BatchErrorFilter,
  options?: { signal?: AbortSignal },
): Promise<PagedResult<BatchErrorDetailDto>> {
  const { data } = await client.get<PagedResult<BatchErrorDetailDto>>('/batch-errors', {
    params: filter,
    signal: options?.signal,
  });
  return data;
}

export async function retryBatch(batchId: number): Promise<void> {
  await client.post(`/outgoing-batches/${batchId}/retry`);
}

export async function retryAllBatches(): Promise<void> {
  await client.post('/outgoing-batches/retry-all');
}
