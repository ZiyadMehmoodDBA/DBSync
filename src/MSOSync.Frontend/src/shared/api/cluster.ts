import client from './client';
import type { ClusterSummaryDto } from '../types/cluster';

export const clusterKeys = {
  summary: ['cluster', 'summary'] as const,
} as const;

export async function getClusterSummary(options?: { signal?: AbortSignal }): Promise<ClusterSummaryDto> {
  const { data } = await client.get<ClusterSummaryDto>('/cluster/summary', options);
  return data;
}
