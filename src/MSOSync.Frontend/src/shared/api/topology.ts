import client from './client';
import type { TopologySummaryDto, TopologyGroupDto, TopologyGroupNodeDto } from '../types';

export async function getTopologySummary(): Promise<TopologySummaryDto> {
  const { data } = await client.get<TopologySummaryDto>('/topology/summary');
  return data;
}

export async function getTopologyGroups(): Promise<TopologyGroupDto[]> {
  const { data } = await client.get<TopologyGroupDto[]>('/topology/groups');
  return data;
}

export async function getTopologyGroupNodes(groupId: string): Promise<TopologyGroupNodeDto[]> {
  const { data } = await client.get<TopologyGroupNodeDto[]>(`/topology/groups/${groupId}/nodes`);
  return data;
}
