import client from './client';
import type { CreateRollingOperationRequest, RollingOperationDetailDto } from '../types/rolling';

export const rollingKeys = {
  all: ['rolling-operations'] as const,
  detail: (id: string) => ['rolling-operations', id] as const,
};

export async function createRollingOperation(
  body: CreateRollingOperationRequest,
): Promise<{ operationId: string }> {
  const { data } = await client.post<{ operationId: string }>('/operations/rolling', body);
  return data;
}

export async function getRollingOperation(
  id: string, options?: { signal?: AbortSignal },
): Promise<RollingOperationDetailDto> {
  const { data } = await client.get<RollingOperationDetailDto>(
    `/operations/rolling/${encodeURIComponent(id)}`, options);
  return data;
}

export async function pauseRollingOperation(id: string): Promise<void> {
  await client.post(`/operations/rolling/${encodeURIComponent(id)}/pause`);
}

export async function resumeRollingOperation(id: string): Promise<void> {
  await client.post(`/operations/rolling/${encodeURIComponent(id)}/resume`);
}

export async function abortRollingOperation(id: string): Promise<void> {
  await client.post(`/operations/rolling/${encodeURIComponent(id)}/abort`);
}

export async function confirmRollingStep(stepId: string): Promise<void> {
  await client.post(`/operations/rolling/steps/${encodeURIComponent(stepId)}/confirm`);
}
