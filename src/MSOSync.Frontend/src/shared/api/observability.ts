import client from './client';
import type { NodeHealthScore, SloStatus } from '../types/observability';

export async function getHealthScores(): Promise<NodeHealthScore[]> {
  const { data } = await client.get<NodeHealthScore[]>('/health/scores');
  return data;
}

export async function getSloStatus(): Promise<SloStatus> {
  const { data } = await client.get<SloStatus>('/slo/status');
  return data;
}
