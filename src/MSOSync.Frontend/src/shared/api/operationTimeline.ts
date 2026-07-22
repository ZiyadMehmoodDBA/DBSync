import client from './client';
import type { OperationTimelineDto } from '../types/timeline';

export const timelineKeys = {
  list: (from: string, to: string, types: string[]) =>
    ['operation-timeline', from, to, types] as const,
} as const;

export async function getOperationTimeline(
  from:     string,
  to:       string,
  types?:   string[],
  limit?:   number,
  options?: { signal?: AbortSignal },
): Promise<OperationTimelineDto> {
  const params: Record<string, string | number | undefined> = {
    from,
    to,
    limit: limit ?? 200,
  };
  if (types && types.length > 0) params.types = types.join(',');

  const { data } = await client.get<OperationTimelineDto>('/operations/timeline', {
    params,
    ...options,
  });
  return data;
}
