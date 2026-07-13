import client from './client';
import type { OperationPageDto, OperationDto, OperationFilter } from '../types/operations';

export async function fetchOperations(filter: OperationFilter): Promise<OperationPageDto> {
  const params: Record<string, string | string[]> = {};
  if (filter.types?.length)    params['types']    = filter.types;
  if (filter.statuses?.length) params['statuses'] = filter.statuses;
  if (filter.from)             params['from']     = filter.from;
  if (filter.to)               params['to']       = filter.to;
  if (filter.pageSize != null) params['pageSize'] = String(filter.pageSize);
  if (filter.cursor)           params['cursor']   = filter.cursor;

  const { data } = await client.get<OperationPageDto>('/operations', { params });
  return data;
}

export async function fetchOperationDetail(id: string): Promise<OperationDto> {
  const { data } = await client.get<OperationDto>(`/operations/${encodeURIComponent(id)}`);
  return data;
}

export async function cancelOperation(id: string): Promise<OperationDto> {
  const { data } = await client.post<OperationDto>(
    `/operations/${encodeURIComponent(id)}/cancel`,
  );
  return data;
}

export async function retryOperation(id: string): Promise<OperationDto> {
  const { data } = await client.post<OperationDto>(
    `/operations/${encodeURIComponent(id)}/retry`,
  );
  return data;
}

export const operationKeys = {
  all:    ['operations'] as const,
  list:   (filter: OperationFilter) => ['operations', 'list', filter] as const,
  detail: (id: string) => ['operations', 'detail', id] as const,
};
