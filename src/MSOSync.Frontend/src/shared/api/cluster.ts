import client from './client';
import type { ClusterSummaryDto, ClusterHealthTrendDto, RecoveryDashboardDto } from '../types/cluster';

export const clusterKeys = {
  summary:      ['cluster', 'summary']                                              as const,
  healthTrends: (window: string, nodeId?: string) =>
                  ['cluster', 'health-trends', window, nodeId ?? null]              as const,
  recovery:     ['cluster', 'recovery']                                             as const,
} as const;

export async function getClusterSummary(options?: { signal?: AbortSignal }): Promise<ClusterSummaryDto> {
  const { data } = await client.get<ClusterSummaryDto>('/cluster/summary', options);
  return data;
}

export async function getHealthTrends(
  window: string,
  nodeId?: string,
  options?: { signal?: AbortSignal },
): Promise<ClusterHealthTrendDto> {
  const params = new URLSearchParams({ window });
  if (nodeId) params.set('nodeId', nodeId);
  const { data } = await client.get<ClusterHealthTrendDto>(`/cluster/health-trends?${params}`, options);
  return data;
}

export async function getRecoveryDashboard(options?: { signal?: AbortSignal }): Promise<RecoveryDashboardDto> {
  const { data } = await client.get<RecoveryDashboardDto>('/cluster/recovery', options);
  return data;
}
