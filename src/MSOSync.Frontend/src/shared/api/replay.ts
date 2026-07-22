import client from './client';
import type {
  CreateReplayOperationRequest,
  ReplayOperationCreatedDto,
  ReplayOperationDetailDto,
  ReplayItemPage,
} from '../types/replay';

export const replayKeys = {
  all:    ['replay-operations'] as const,
  detail: (id: string) => ['replay-operations', id] as const,
  items:  (id: string) => ['replay-operations', id, 'items'] as const,
};

export async function createReplay(
  body: CreateReplayOperationRequest,
): Promise<ReplayOperationCreatedDto> {
  const { data } = await client.post<ReplayOperationCreatedDto>('/operations/replay', body);
  return data;
}

export async function getReplayDetail(
  id: string, options?: { signal?: AbortSignal },
): Promise<ReplayOperationDetailDto> {
  const { data } = await client.get<ReplayOperationDetailDto>(
    `/operations/replay/${encodeURIComponent(id)}`, options);
  return data;
}

export async function getReplayItems(
  id: string, params?: { status?: string; cursor?: string; pageSize?: number },
  options?: { signal?: AbortSignal },
): Promise<ReplayItemPage> {
  const { data } = await client.get<ReplayItemPage>(
    `/operations/replay/${encodeURIComponent(id)}/items`,
    { params, ...options });
  return data;
}

export async function cancelReplay(id: string): Promise<void> {
  await client.post(`/operations/replay/${encodeURIComponent(id)}/cancel`);
}
