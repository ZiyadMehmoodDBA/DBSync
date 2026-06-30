import client from './client';
import type {
  IncomingBatchSummaryDto,
  OutgoingBatchDto,
  BatchErrorDetailDto,
  IncomingBatchFilter,
  OutgoingBatchFilter,
  BatchErrorFilter,
} from '../types';
import type { PagedResult } from '../types/common';

export async function getIncomingBatches(
  filter: IncomingBatchFilter,
): Promise<PagedResult<IncomingBatchSummaryDto>> {
  const { data } = await client.get<PagedResult<IncomingBatchSummaryDto>>('/incoming-batches', {
    params: filter,
  });
  return data;
}

export async function getOutgoingBatches(
  filter: OutgoingBatchFilter,
): Promise<PagedResult<OutgoingBatchDto>> {
  const { data } = await client.get<PagedResult<OutgoingBatchDto>>('/batches', {
    params: filter,
  });
  return data;
}

export async function getBatchErrors(
  filter: BatchErrorFilter,
): Promise<PagedResult<BatchErrorDetailDto>> {
  const { data } = await client.get<PagedResult<BatchErrorDetailDto>>('/batch-errors', {
    params: filter,
  });
  return data;
}

export async function retryBatch(batchId: number): Promise<void> {
  await client.post(`/outgoing-batches/${batchId}/retry`);
}

export async function retryAllBatches(): Promise<void> {
  await client.post('/outgoing-batches/retry-all');
}
